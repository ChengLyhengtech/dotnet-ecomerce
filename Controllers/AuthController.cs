using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Models;
using Ecomerce_dotnet.Services;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("ApiPolicy")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly RoleManager<Role> _roleManager;
        private readonly IConfiguration _configuration;
        private readonly IJwtTokenService _tokenService;

        public AuthController(
            UserManager<User> userManager,
            RoleManager<Role> roleManager,
            IConfiguration configuration,
            IJwtTokenService tokenService)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _configuration = configuration;
            _tokenService = tokenService;
        }

        private async Task<AuthResponseDto> BuildAuthResponseAsync(
            User user,
            string message,
            string accessToken,
            string refreshToken,
            DateTime refreshExpiration)
        {
            var roles = (await _userManager.GetRolesAsync(user)).ToList();
            var permissions = await _tokenService.GetUserPermissionsAsync(user.Id);

            return new AuthResponseDto
            {
                Success = true,
                Message = message,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                RefreshTokenExpiration = refreshExpiration,
                RequiresPasswordChange = user.MustChangePassword,
                Roles = roles,
                Permissions = permissions,
                User = new UserInfoDto
                {
                    Id = user.Id,
                    Email = user.Email ?? string.Empty,
                    FullName = user.FullName,
                    AvatarUrl = user.AvatarUrl,
                    RequiresPasswordChange = user.MustChangePassword,
                    Roles = roles,
                    Permissions = permissions
                }
            };
        }

        // 1. POST: api/auth/register (Registers a user with default 'Customer' role)
        [HttpPost("register")]
        [EnableRateLimiting("LoginPolicy")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var existingUser = await _userManager.FindByEmailAsync(dto.Email);
            if (existingUser != null)
            {
                return BadRequest(new AuthResponseDto
                {
                    Success = false,
                    Message = "An account with this email address already exists."
                });
            }

            var user = new User
            {
                UserName = dto.Email,
                Email = dto.Email,
                FullName = dto.FullName.Trim(),
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow,
                IsBanned = false
            };

            var result = await _userManager.CreateAsync(user, dto.Password);
            if (!result.Succeeded)
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                return BadRequest(new AuthResponseDto
                {
                    Success = false,
                    Message = $"Registration failed: {errors}"
                });
            }

            // Assign default application role 'Customer'
            const string defaultRole = "Customer";
            if (await _roleManager.RoleExistsAsync(defaultRole))
            {
                await _userManager.AddToRoleAsync(user, defaultRole);
            }

            // Generate Access Token and Refresh Token
            var accessToken = await _tokenService.GenerateAccessTokenAsync(user);
            var refreshToken = _tokenService.GenerateRefreshToken();

            int refreshExpirationDays = int.TryParse(_configuration["JwtSettings:RefreshTokenExpirationDays"], out var days) ? days : 7;
            var refreshExpiration = DateTime.UtcNow.AddDays(refreshExpirationDays);

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = refreshExpiration;
            await _userManager.UpdateAsync(user);

            var response = await BuildAuthResponseAsync(
                user,
                "Registration successful.",
                accessToken,
                refreshToken,
                refreshExpiration);

            return Ok(response);
        }

        // 2. POST: api/auth/login (Authenticates credentials & returns Tokens)
        [HttpPost("login")]
        [EnableRateLimiting("LoginPolicy")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null)
            {
                return Unauthorized(new AuthResponseDto
                {
                    Success = false,
                    Message = "Invalid email or password credentials."
                });
            }

            if (user.IsBanned)
            {
                return StatusCode(403, new AuthResponseDto
                {
                    Success = false,
                    Message = "Your account has been suspended by an administrator."
                });
            }

            bool isPasswordValid = await _userManager.CheckPasswordAsync(user, dto.Password);
            if (!isPasswordValid)
            {
                return Unauthorized(new AuthResponseDto
                {
                    Success = false,
                    Message = "Invalid email or password credentials."
                });
            }

            // Issue fresh JWT Access Token and Refresh Token
            var accessToken = await _tokenService.GenerateAccessTokenAsync(user);
            var refreshToken = _tokenService.GenerateRefreshToken();

            int refreshExpirationDays = int.TryParse(_configuration["JwtSettings:RefreshTokenExpirationDays"], out var days) ? days : 7;
            var refreshExpiration = DateTime.UtcNow.AddDays(refreshExpirationDays);

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = refreshExpiration;
            await _userManager.UpdateAsync(user);

            var response = await BuildAuthResponseAsync(
                user,
                "Login successful.",
                accessToken,
                refreshToken,
                refreshExpiration);

            return Ok(response);
        }

        // 3. POST: api/auth/refresh-token (Rotates Access Token using valid Refresh Token)
        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var principal = _tokenService.GetPrincipalFromExpiredToken(dto.AccessToken);
                if (principal == null)
                {
                    return BadRequest(new AuthResponseDto
                    {
                        Success = false,
                        Message = "Invalid access token."
                    });
                }

                var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                {
                    return BadRequest(new AuthResponseDto
                    {
                        Success = false,
                        Message = "Invalid token payload identity."
                    });
                }

                var user = await _userManager.FindByIdAsync(userId.ToString());
                if (user == null || user.RefreshToken != dto.RefreshToken || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
                {
                    return BadRequest(new AuthResponseDto
                    {
                        Success = false,
                        Message = "Invalid or expired refresh token. Please login again."
                    });
                }

                if (user.IsBanned)
                {
                    return StatusCode(403, new AuthResponseDto
                    {
                        Success = false,
                        Message = "Your account has been suspended."
                    });
                }

                // Issue rotated token pair
                var newAccessToken = await _tokenService.GenerateAccessTokenAsync(user);
                var newRefreshToken = _tokenService.GenerateRefreshToken();

                int refreshExpirationDays = int.TryParse(_configuration["JwtSettings:RefreshTokenExpirationDays"], out var days) ? days : 7;
                var refreshExpiration = DateTime.UtcNow.AddDays(refreshExpirationDays);

                user.RefreshToken = newRefreshToken;
                user.RefreshTokenExpiryTime = refreshExpiration;
                await _userManager.UpdateAsync(user);

                var response = await BuildAuthResponseAsync(
                    user,
                    "Token refreshed successfully.",
                    newAccessToken,
                    newRefreshToken,
                    refreshExpiration);

                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new AuthResponseDto
                {
                    Success = false,
                    Message = $"Token refresh error: {ex.Message}"
                });
            }
        }

        // 4. POST: api/auth/revoke-token (Revokes user refresh token session)
        [Authorize]
        [HttpPost("revoke-token")]
        public async Task<IActionResult> RevokeToken()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized();
            }

            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user == null) return NotFound("User not found.");

            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;
            await _userManager.UpdateAsync(user);

            return Ok(new { success = true, message = "Refresh token revoked successfully." });
        }

        // 5. POST: api/auth/force-change-password (First-login forced password change)
        [Authorize]
        [HttpPost("force-change-password")]
        public async Task<IActionResult> ForceChangePassword([FromBody] ForceChangePasswordDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { success = false, message = "Invalid user identity in token." });
            }

            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user == null)
            {
                return NotFound(new { success = false, message = "User not found." });
            }

            // Change password using ASP.NET Core Identity password hasher verification
            var changeResult = await _userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);
            if (!changeResult.Succeeded)
            {
                var errors = string.Join("; ", changeResult.Errors.Select(e => e.Description));
                return BadRequest(new { success = false, message = $"Failed to change password: {errors}" });
            }

            // Unlock account by turning off MustChangePassword
            user.MustChangePassword = false;

            // Generate fresh token pair
            var newAccessToken = await _tokenService.GenerateAccessTokenAsync(user);
            var newRefreshToken = _tokenService.GenerateRefreshToken();

            int refreshExpirationDays = int.TryParse(_configuration["JwtSettings:RefreshTokenExpirationDays"], out var days) ? days : 7;
            var refreshExpiration = DateTime.UtcNow.AddDays(refreshExpirationDays);

            user.RefreshToken = newRefreshToken;
            user.RefreshTokenExpiryTime = refreshExpiration;
            await _userManager.UpdateAsync(user);

            var response = await BuildAuthResponseAsync(
                user,
                "Password changed successfully. Your account is now fully unlocked.",
                newAccessToken,
                newRefreshToken,
                refreshExpiration);

            return Ok(response);
        }

        // 6. POST: api/auth/google-login
        [HttpPost("google-login")]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginDto dto)
        {
            GoogleJsonWebSignature.Payload payload;

            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken, new GoogleJsonWebSignature.ValidationSettings());
            }
            catch (InvalidJwtException)
            {
                return BadRequest("Invalid Google token.");
            }

            var user = await _userManager.FindByEmailAsync(payload.Email);

            if (user == null)
            {
                user = new User
                {
                    UserName = payload.Email,
                    Email = payload.Email,
                    FullName = payload.Name,
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow,
                    IsBanned = false
                };

                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                    return BadRequest("Failed to create user account from Google provider.");

                await _userManager.AddToRoleAsync(user, "Customer");
            }

            if (user.IsBanned)
            {
                return StatusCode(403, "Your account has been suspended.");
            }

            var accessToken = await _tokenService.GenerateAccessTokenAsync(user);
            var refreshToken = _tokenService.GenerateRefreshToken();

            int refreshExpirationDays = int.TryParse(_configuration["JwtSettings:RefreshTokenExpirationDays"], out var days) ? days : 7;
            var refreshExpiration = DateTime.UtcNow.AddDays(refreshExpirationDays);

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = refreshExpiration;
            await _userManager.UpdateAsync(user);

            var response = await BuildAuthResponseAsync(
                user,
                "Google login successful.",
                accessToken,
                refreshToken,
                refreshExpiration);

            return Ok(response);
        }

        // 6. POST: api/auth/telegram-login
        [HttpPost("telegram-login")]
        public async Task<IActionResult> TelegramLogin([FromBody] TelegramLoginDto dto)
        {
            string botToken = _configuration["Telegram:BotToken"] ?? string.Empty;

            if (string.IsNullOrEmpty(botToken))
                return StatusCode(500, "Telegram Bot Token is not configured on the server.");

            if (!VerifyTelegramHash(dto, botToken))
            {
                return BadRequest("Invalid Telegram security signature hash validation failed.");
            }

            var timeSpan = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - dto.Auth_Date;
            if (timeSpan > 86400)
                return BadRequest("Authentication token session has expired.");

            string syntheticEmail = dto.Username != null
                ? $"{dto.Username.ToLower()}@telegram.com"
                : $"{dto.Id}@telegram.com";

            var user = await _userManager.FindByEmailAsync(syntheticEmail);

            if (user == null)
            {
                user = new User
                {
                    UserName = syntheticEmail,
                    Email = syntheticEmail,
                    FullName = $"{dto.First_Name} {dto.Last_Name}".Trim(),
                    AvatarUrl = dto.Photo_Url,
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow,
                    IsBanned = false
                };

                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                    return BadRequest("Failed to register account from Telegram provider context.");

                await _userManager.AddToRoleAsync(user, "Customer");
            }

            if (user.IsBanned)
            {
                return StatusCode(403, "Your account has been suspended.");
            }

            var accessToken = await _tokenService.GenerateAccessTokenAsync(user);
            var refreshToken = _tokenService.GenerateRefreshToken();

            int refreshExpirationDays = int.TryParse(_configuration["JwtSettings:RefreshTokenExpirationDays"], out var days) ? days : 7;
            var refreshExpiration = DateTime.UtcNow.AddDays(refreshExpirationDays);

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = refreshExpiration;
            await _userManager.UpdateAsync(user);

            var response = await BuildAuthResponseAsync(
                user,
                "Telegram login successful.",
                accessToken,
                refreshToken,
                refreshExpiration);

            return Ok(response);
        }

        private bool VerifyTelegramHash(TelegramLoginDto dto, string token)
        {
            var dataFields = new SortedDictionary<string, string>
            {
                { "auth_date", dto.Auth_Date.ToString() },
                { "first_name", dto.First_Name },
                { "id", dto.Id.ToString() }
            };

            if (!string.IsNullOrEmpty(dto.Last_Name)) dataFields.Add("last_name", dto.Last_Name);
            if (!string.IsNullOrEmpty(dto.Username)) dataFields.Add("username", dto.Username);
            if (!string.IsNullOrEmpty(dto.Photo_Url)) dataFields.Add("photo_url", dto.Photo_Url);

            string dataCheckString = string.Join("\n", dataFields.Select(x => $"{x.Key}={x.Value}"));

            byte[] secretKey;
            using (var sha256 = SHA256.Create())
            {
                secretKey = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
            }

            using (var hmac = new HMACSHA256(secretKey))
            {
                byte[] computedSignatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString));
                string computedHashString = BitConverter.ToString(computedSignatureBytes).Replace("-", "").ToLower();

                return computedHashString == dto.Hash.ToLower();
            }
        }
    }
}
