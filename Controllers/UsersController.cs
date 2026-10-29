using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Models;
using Ecomerce_dotnet.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/admin/[controller]")]
    [Authorize(Roles = "Admin")]
    [EnableRateLimiting("ApiPolicy")]
    public class UsersController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly IFileService _fileService;

        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        private const long MaxFileSizeInBytes = 5 * 1024 * 1024; // 5 MB

        public UsersController(
            UserManager<User> userManager,
            IPasswordHasher<User> passwordHasher,
            IFileService fileService)
        {
            _userManager = userManager;
            _passwordHasher = passwordHasher;
            _fileService = fileService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            // 1. Fetch the paginated user list from the database
            var users = await _userManager.Users
                .OrderByDescending(u => u.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var response = new List<UserResponseDto>();

            // 2. Loop through each user to attach their roles
            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user); // Hits the join table

                response.Add(new UserResponseDto
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    Email = user.Email ?? string.Empty,
                    AvatarUrl = user.AvatarUrl,
                    IsBanned = user.IsBanned,
                    CreatedAt = user.CreatedAt,
                    Roles = roles.ToList() // This puts ["Admin"] or ["Customer"] in the JSON!
                });
            }

            return Ok(response);
        }

        // 2. CREATE ACCOUNT WITH ROLE
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UserCreateAdminDto dto)
        {
            var existingUser = await _userManager.FindByEmailAsync(dto.Email);
            if (existingUser != null) return BadRequest("Email already registered.");

            var user = new User
            {
                UserName = dto.Email, // Identity requires UserName to be set
                Email = dto.Email,
                FullName = dto.FullName,
                IsBanned = false,
                CreatedAt = DateTime.UtcNow
            };

            // UserManager automatically hashes the password for you!
            var result = await _userManager.CreateAsync(user, dto.Password);

            if (!result.Succeeded)
                return BadRequest(result.Errors);

            // Assign the role using Identity's native system
            await _userManager.AddToRoleAsync(user, dto.Role);

            return CreatedAtAction(nameof(GetAll), new { id = user.Id }, new UserResponseDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email ?? string.Empty,
                AvatarUrl = user.AvatarUrl,
                IsBanned = user.IsBanned,
                CreatedAt = user.CreatedAt,
                Roles = new List<string> { dto.Role }
            });
        }

        // 3. UPDATE USER ROLE
        [HttpPut("{id:guid}/role")]
        public async Task<IActionResult> UpdateRole(Guid id, [FromBody] UserUpdateRoleDto dto)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound();

            // Remove from existing roles first
            var currentRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, currentRoles);

            // Add to the new role
            await _userManager.AddToRoleAsync(user, dto.Role);

            return NoContent();
        }

        // 4. BAN / UNBAN USER
        [HttpPut("{id:guid}/ban")]
        public async Task<IActionResult> ToggleBan(Guid id, [FromBody] UserBanDto dto)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound();

            // This works now because we added IsBanned to your User model!
            user.IsBanned = dto.IsBanned;

            await _userManager.UpdateAsync(user);

            return Ok(new { message = dto.IsBanned ? "User banned." : "User unbanned." });
        }

        // 5. RESET USER PASSWORD (FLOW 2: TEMPORARY PASSWORD + FORCED CHANGE - ADMIN ONLY)
        // POST: api/admin/users/{id}/reset-password
        [HttpPost("{id:guid}/reset-password")]
        public async Task<IActionResult> ResetPassword(Guid id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
            {
                return NotFound(new { success = false, message = "User not found." });
            }

            // 1. Generate a cryptographically strong random temporary password
            string temporaryPassword = PasswordGenerator.GenerateTemporaryPassword(12);

            // 2. Hash the temporary password using ASP.NET Core IPasswordHasher<User>
            user.PasswordHash = _passwordHasher.HashPassword(user, temporaryPassword);

            // 3. Flag user so they are required to change their password upon login
            user.MustChangePassword = true;

            // 4. Invalidate existing active refresh token sessions to force re-authentication
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                return BadRequest(new { success = false, message = $"Failed to reset password: {errors}" });
            }

            // 5. Return plain-text temporary password so admin can communicate it to the user
            return Ok(new
            {
                success = true,
                message = $"Temporary password generated for user '{user.Email}'. The user will be required to change it upon first login.",
                userId = user.Id,
                email = user.Email,
                temporaryPassword = temporaryPassword,
                mustChangePassword = true
            });
        }

        // ========================================================
        // 6. ADMIN: UPLOAD / UPDATE USER AVATAR
        // POST: api/admin/users/{id}/avatar
        // ========================================================
        [HttpPost("{id:guid}/avatar")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> AdminUploadAvatar(Guid id, IFormFile? file, IFormFile? avatar)
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

            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
            {
                return NotFound(new { success = false, message = "User not found." });
            }

            if (!string.IsNullOrEmpty(user.AvatarUrl) && user.AvatarUrl.StartsWith("/uploads/avatars/", StringComparison.OrdinalIgnoreCase))
            {
                _fileService.DeletePhysicalFile(user.AvatarUrl);
            }

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
                userId = user.Id,
                avatarUrl = user.AvatarUrl
            });
        }

        // ========================================================
        // 7. ADMIN: REMOVE USER AVATAR
        // DELETE: api/admin/users/{id}/avatar
        // ========================================================
        [HttpDelete("{id:guid}/avatar")]
        public async Task<IActionResult> AdminRemoveAvatar(Guid id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
            {
                return NotFound(new { success = false, message = "User not found." });
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
                userId = user.Id,
                avatarUrl = (string?)null
            });
        }

        // ========================================================
        // 8. ADMIN: UPDATE USER FULL NAME
        // PUT: api/admin/users/{id}/fullname
        // PATCH: api/admin/users/{id}/fullname
        // ========================================================
        [HttpPut("{id:guid}/fullname")]
        [HttpPatch("{id:guid}/fullname")]
        public async Task<IActionResult> AdminUpdateFullName(Guid id, [FromBody] UpdateFullNameDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (string.IsNullOrWhiteSpace(dto.FullName))
            {
                return BadRequest(new { success = false, message = "Full name cannot be empty." });
            }

            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
            {
                return NotFound(new { success = false, message = "User not found." });
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
                userId = user.Id,
                fullName = user.FullName
            });
        }
    }
}
