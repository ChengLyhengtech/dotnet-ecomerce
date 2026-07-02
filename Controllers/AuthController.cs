using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Models;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly IConfiguration _configuration;
        // private readonly IJwtTokenService _tokenService; // Inject your future JWT token generator here!

        public AuthController(UserManager<User> userManager, IConfiguration configuration)
        {
            _userManager = userManager;
            _configuration = configuration;
        }

        [HttpPost("google-login")]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginDto dto)
        {
            GoogleJsonWebSignature.Payload payload;

            try
            {
                // 1. Validate the token against Google's public keys to ensure it's authentic and not forged
                payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken, new GoogleJsonWebSignature.ValidationSettings
                {
                    // Highly recommended: Put your Google Client ID here to ensure the token was meant for YOUR app
                    // Audience = new[] { "YOUR_GOOGLE_CLIENT_ID.apps.googleusercontent.com" }
                });
            }
            catch (InvalidJwtException)
            {
                return BadRequest("Invalid Google token.");
            }

            // 2. Look up the user in your AspNetUsers database using the email verified by Google
            var user = await _userManager.FindByEmailAsync(payload.Email);

            if (user == null)
            {
                // 3. Register Flow: If the user doesn't exist, instantly register them!
                user = new User
                {
                    UserName = payload.Email,
                    Email = payload.Email,
                    FullName = payload.Name, // Pulls name directly from Google account
                    EmailConfirmed = true,    // Google already verified this email
                    CreatedAt = DateTime.UtcNow,
                    IsBanned = false
                };

                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                    return BadRequest("Failed to automatically create user account.");

                // Assign default role
                await _userManager.AddToRoleAsync(user, "Customer");
            }

            // 4. Check if Admin has banned them before generating tokens
            if (user.IsBanned)
            {
                return Forbid("Your account has been suspended.");
            }

            // 5. Generate your own system JWT token here!
            // var appToken = _tokenService.GenerateToken(user);

            return Ok(new
            {
                message = "Login successful",
                email = user.Email,
                fullName = user.FullName,
                // token = appToken // Send this back to frontend to save in localStorage
            });
        }

        [HttpPost("telegram-login")]
        public async Task<IActionResult> TelegramLogin([FromBody] TelegramLoginDto dto)
        {
            // 1. Get the Bot Token from your configuration settings
            string botToken = _configuration["Telegram:BotToken"];

            if (string.IsNullOrEmpty(botToken))
                return StatusCode(500, "Telegram Bot Token is not configured on the server.");

            // 2. Validate the Cryptographic Signature from Telegram
            if (!VerifyTelegramHash(dto, botToken))
            {
                return BadRequest("Invalid Telegram security signature hash validation failed.");
            }

            // 3. Optional: Check if the authentication timestamp is older than 24 hours to prevent replay attacks
            var timeSpan = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - dto.Auth_Date;
            if (timeSpan > 86400)
                return BadRequest("Authentication token session has expired.");

            // 4. Look up user by unique username or generate a deterministic dummy email format
            string syntheticEmail = dto.Username != null
                ? $"{dto.Username.ToLower()}@telegram.com"
                : $"{dto.Id}@telegram.com";

            var user = await _userManager.FindByEmailAsync(syntheticEmail);

            if (user == null)
            {
                // Auto-Register user seamlessly
                user = new User
                {
                    UserName = syntheticEmail,
                    Email = syntheticEmail,
                    FullName = $"{dto.First_Name} {dto.Last_Name}".Trim(),
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
                return Forbid("Your account has been suspended.");
            }

            // Generate your own system JWT token here!
            return Ok(new
            {
                message = "Login successful via Telegram",
                email = user.Email,
                fullName = user.FullName
            });
        }

        // Helper Method: Implements Telegram's official validation specification algorithm
        private bool VerifyTelegramHash(TelegramLoginDto dto, string token)
        {
            // Gather incoming parameters into an alphabetically ordered dictionary mapping
            var dataFields = new SortedDictionary<string, string>
            {
                { "auth_date", dto.Auth_Date.ToString() },
                { "first_name", dto.First_Name },
                { "id", dto.Id.ToString() }
            };

            if (!string.IsNullOrEmpty(dto.Last_Name)) dataFields.Add("last_name", dto.Last_Name);
            if (!string.IsNullOrEmpty(dto.Username)) dataFields.Add("username", dto.Username);
            if (!string.IsNullOrEmpty(dto.Photo_Url)) dataFields.Add("photo_url", dto.Photo_Url);

            // Construct the check-string stringified schema separated by newlines (\n)
            string dataCheckString = string.Join("\n", dataFields.Select(x => $"{x.Key}={x.Value}"));

            // Step A: Hash the Bot Token using standard SHA256
            byte[] secretKey;
            using (var sha256 = SHA256.Create())
            {
                secretKey = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
            }

            // Step B: Calculate the HMAC-SHA256 signature using the secret key computed above
            using (var hmac = new HMACSHA256(secretKey))
            {
                byte[] computedSignatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString));
                string computedHashString = BitConverter.ToString(computedSignatureBytes).Replace("-", "").ToLower();

                // Compare computed hexadecimal result with the received value parameter
                return computedHashString == dto.Hash.ToLower();
            }
        }
    }
}

