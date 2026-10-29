using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Models;
using Ecomerce_dotnet.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Route("api/user")]
    [Authorize]
    [EnableRateLimiting("ApiPolicy")]
    public class UserController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly IFileService _fileService;
        private readonly IJwtTokenService _tokenService;

        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        private const long MaxFileSizeInBytes = 5 * 1024 * 1024; // 5 MB

        public UserController(
            UserManager<User> userManager,
            IFileService fileService,
            IJwtTokenService tokenService)
        {
            _userManager = userManager;
            _fileService = fileService;
            _tokenService = tokenService;
        }

        private Guid? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                           ?? User.FindFirst("sub")?.Value;
            if (Guid.TryParse(userIdClaim, out var userId))
            {
                return userId;
            }
            return null;
        }

        // ========================================================
        // 1. GET CURRENT USER PROFILE
        // GET: api/users/profile, api/users/me, api/user/profile, api/user/me
        // ========================================================
        [HttpGet("profile")]
        [HttpGet("me")]
        public async Task<IActionResult> GetProfile()
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return Unauthorized(new { success = false, message = "User context is invalid or token missing." });
            }

            var user = await _userManager.FindByIdAsync(userId.Value.ToString());
            if (user == null)
            {
                return NotFound(new { success = false, message = "User record not found." });
            }

            var roles = (await _userManager.GetRolesAsync(user)).ToList();
            var permissions = await _tokenService.GetUserPermissionsAsync(user.Id);

            return Ok(new UserProfileResponseDto
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                FullName = user.FullName,
                AvatarUrl = user.AvatarUrl,
                IsBanned = user.IsBanned,
                CreatedAt = user.CreatedAt,
                Roles = roles,
                Permissions = permissions
            });
        }

        // ========================================================
        // 2. UPLOAD USER AVATAR
        // POST: api/users/avatar, api/user/avatar, api/users/profile/avatar
        // ========================================================
        [HttpPost("avatar")]
        [HttpPost("profile/avatar")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadAvatar(IFormFile? file, IFormFile? avatar)
        {
            var targetFile = file ?? avatar;
            if (targetFile == null || targetFile.Length == 0)
            {
                return BadRequest(new { success = false, message = "Avatar image file is required." });
            }

            if (targetFile.Length > MaxFileSizeInBytes)
            {
                return BadRequest(new { success = false, message = "Avatar file size exceeds the 5MB limit." });
            }

            var extension = Path.GetExtension(targetFile.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
            {
                return BadRequest(new
                {
                    success = false,
                    message = $"Invalid image format. Allowed formats: {string.Join(", ", AllowedExtensions)}"
                });
            }

            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return Unauthorized(new { success = false, message = "User context is invalid or token missing." });
            }

            var user = await _userManager.FindByIdAsync(userId.Value.ToString());
            if (user == null)
            {
                return NotFound(new { success = false, message = "User record not found." });
            }

            // Remove existing physical avatar if it was stored locally
            if (!string.IsNullOrEmpty(user.AvatarUrl) && user.AvatarUrl.StartsWith("/uploads/avatars/", StringComparison.OrdinalIgnoreCase))
            {
                _fileService.DeletePhysicalFile(user.AvatarUrl);
            }

            // Save new avatar file into 'avatars' subfolder
            string relativeUrl = await _fileService.SaveFileAsync(targetFile, "avatars");

            user.AvatarUrl = relativeUrl;
            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                _fileService.DeletePhysicalFile(relativeUrl);
                var errors = string.Join("; ", updateResult.Errors.Select(e => e.Description));
                return BadRequest(new { success = false, message = $"Failed to update user avatar: {errors}" });
            }

            return Ok(new
            {
                success = true,
                message = "Avatar uploaded successfully.",
                avatarUrl = user.AvatarUrl
            });
        }

        // ========================================================
        // 3. DELETE / REMOVE USER AVATAR
        // DELETE: api/users/avatar, api/user/avatar, api/users/profile/avatar
        // ========================================================
        [HttpDelete("avatar")]
        [HttpDelete("profile/avatar")]
        public async Task<IActionResult> RemoveAvatar()
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return Unauthorized(new { success = false, message = "User context is invalid or token missing." });
            }

            var user = await _userManager.FindByIdAsync(userId.Value.ToString());
            if (user == null)
            {
                return NotFound(new { success = false, message = "User record not found." });
            }

            if (!string.IsNullOrEmpty(user.AvatarUrl))
            {
                if (user.AvatarUrl.StartsWith("/uploads/avatars/", StringComparison.OrdinalIgnoreCase))
                {
                    _fileService.DeletePhysicalFile(user.AvatarUrl);
                }
                user.AvatarUrl = null;
                await _userManager.UpdateAsync(user);
            }

            return Ok(new
            {
                success = true,
                message = "Avatar removed successfully.",
                avatarUrl = (string?)null
            });
        }

        // ========================================================
        // 4. UPDATE USER FULL NAME
        // PUT: api/users/fullname, api/user/fullname, api/users/profile/fullname
        // PATCH: api/users/fullname, api/user/fullname, api/users/profile/fullname
        // ========================================================
        [HttpPut("fullname")]
        [HttpPut("profile/fullname")]
        [HttpPatch("fullname")]
        [HttpPatch("profile/fullname")]
        public async Task<IActionResult> UpdateFullName([FromBody] UpdateFullNameDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (string.IsNullOrWhiteSpace(dto.FullName))
            {
                return BadRequest(new { success = false, message = "Full name cannot be empty." });
            }

            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return Unauthorized(new { success = false, message = "User context is invalid or token missing." });
            }

            var user = await _userManager.FindByIdAsync(userId.Value.ToString());
            if (user == null)
            {
                return NotFound(new { success = false, message = "User record not found." });
            }

            user.FullName = dto.FullName.Trim();
            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                var errors = string.Join("; ", updateResult.Errors.Select(e => e.Description));
                return BadRequest(new { success = false, message = $"Failed to update full name: {errors}" });
            }

            return Ok(new
            {
                success = true,
                message = "Full name updated successfully.",
                fullName = user.FullName
            });
        }
    }
}
