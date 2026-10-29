using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Filters;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/admin/roles")]
    [Authorize(Roles = "Admin")]
    [EnableRateLimiting("ApiPolicy")]
    public class AdminRoleController : ControllerBase
    {
        private readonly RoleManager<Role> _roleManager;
        private readonly UserManager<User> _userManager;
        private readonly ApplicationDbContext _context;

        public AdminRoleController(
            RoleManager<Role> roleManager,
            UserManager<User> userManager,
            ApplicationDbContext context)
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _context = context;
        }

        // ==========================================
        // 1. ROLE MANAGEMENT ENDPOINTS
        // ==========================================

        /// <summary>
        /// Retrieves all roles with their associated permissions.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAllRoles()
        {
            var roles = await _context.Roles
                .AsNoTracking()
                .Include(r => r.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
                .OrderBy(r => r.Name)
                .Select(r => new RoleResponseDto
                {
                    Id = r.Id,
                    Name = r.Name ?? string.Empty,
                    Description = r.Description,
                    Permissions = r.RolePermissions.Select(rp => new PermissionResponseDto
                    {
                        Id = rp.Permission.Id,
                        Name = rp.Permission.Name,
                        Category = rp.Permission.Category,
                        Description = rp.Permission.Description
                    }).ToList()
                })
                .ToListAsync();

            return Ok(roles);
        }

        /// <summary>
        /// Creates a new dynamic application role.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateRole([FromBody] CreateRoleDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var trimmedName = dto.Name.Trim();
            if (await _roleManager.RoleExistsAsync(trimmedName))
            {
                return BadRequest(new { success = false, message = $"Role '{trimmedName}' already exists." });
            }

            var role = new Role(trimmedName)
            {
                Description = dto.Description?.Trim()
            };

            var result = await _roleManager.CreateAsync(role);
            if (!result.Succeeded)
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                return BadRequest(new { success = false, message = $"Failed to create role: {errors}" });
            }

            return CreatedAtAction(nameof(GetAllRoles), new { id = role.Id }, new RoleResponseDto
            {
                Id = role.Id,
                Name = role.Name ?? string.Empty,
                Description = role.Description,
                Permissions = new List<PermissionResponseDto>()
            });
        }

        /// <summary>
        /// Deletes a role (prevents deletion of core system roles: Admin, Customer).
        /// </summary>
        [HttpDelete("{roleId:guid}")]
        public async Task<IActionResult> DeleteRole(Guid roleId)
        {
            var role = await _roleManager.FindByIdAsync(roleId.ToString());
            if (role == null)
            {
                return NotFound(new { success = false, message = "Role not found." });
            }

            if (string.Equals(role.Name, "Admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role.Name, "Customer", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { success = false, message = $"Protected system role '{role.Name}' cannot be deleted." });
            }

            var result = await _roleManager.DeleteAsync(role);
            if (!result.Succeeded)
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                return BadRequest(new { success = false, message = $"Failed to delete role: {errors}" });
            }

            return Ok(new { success = true, message = $"Role '{role.Name}' deleted successfully." });
        }

        // ==========================================
        // 2. USER ROLE ASSIGNMENT ENDPOINTS
        // ==========================================

        /// <summary>
        /// Assigns a role to a user (supports multiple roles per user).
        /// POST /api/admin/roles/assign
        /// </summary>
        [HttpPost("assign")]
        public async Task<IActionResult> AssignRoleToUser([FromBody] AssignRoleToUserDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var user = await _userManager.FindByIdAsync(dto.UserId.ToString());
            if (user == null)
            {
                return NotFound(new { success = false, message = $"User with ID '{dto.UserId}' not found." });
            }

            var role = await _roleManager.FindByNameAsync(dto.RoleName.Trim());
            if (role == null)
            {
                return NotFound(new { success = false, message = $"Role '{dto.RoleName}' does not exist." });
            }

            if (await _userManager.IsInRoleAsync(user, role.Name!))
            {
                return BadRequest(new { success = false, message = $"User '{user.Email}' already has the role '{role.Name}'." });
            }

            var result = await _userManager.AddToRoleAsync(user, role.Name!);
            if (!result.Succeeded)
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                return BadRequest(new { success = false, message = $"Failed to assign role: {errors}" });
            }

            var currentRoles = await _userManager.GetRolesAsync(user);

            return Ok(new
            {
                success = true,
                message = $"Role '{role.Name}' assigned to user '{user.Email}' successfully.",
                userId = user.Id,
                email = user.Email,
                roles = currentRoles
            });
        }

        /// <summary>
        /// Removes a role from a user.
        /// POST /api/admin/roles/remove
        /// </summary>
        [HttpPost("remove")]
        public async Task<IActionResult> RemoveRoleFromUser([FromBody] RemoveRoleFromUserDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var user = await _userManager.FindByIdAsync(dto.UserId.ToString());
            if (user == null)
            {
                return NotFound(new { success = false, message = $"User with ID '{dto.UserId}' not found." });
            }

            if (!await _userManager.IsInRoleAsync(user, dto.RoleName.Trim()))
            {
                return BadRequest(new { success = false, message = $"User '{user.Email}' does not have the role '{dto.RoleName}'." });
            }

            var result = await _userManager.RemoveFromRoleAsync(user, dto.RoleName.Trim());
            if (!result.Succeeded)
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                return BadRequest(new { success = false, message = $"Failed to remove role: {errors}" });
            }

            var currentRoles = await _userManager.GetRolesAsync(user);

            return Ok(new
            {
                success = true,
                message = $"Role '{dto.RoleName}' removed from user '{user.Email}' successfully.",
                userId = user.Id,
                email = user.Email,
                roles = currentRoles
            });
        }

        /// <summary>
        /// Gets all roles and flattened permissions for a specific user.
        /// GET /api/admin/roles/user/{userId}
        /// </summary>
        [HttpGet("user/{userId:guid}")]
        public async Task<IActionResult> GetUserRolesAndPermissions(Guid userId)
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user == null)
            {
                return NotFound(new { success = false, message = "User not found." });
            }

            var roles = (await _userManager.GetRolesAsync(user)).ToList();

            var permissions = await _context.UserRoles
                .AsNoTracking()
                .Where(ur => ur.UserId == userId)
                .SelectMany(ur => ur.Role.RolePermissions.Select(rp => rp.Permission.Name))
                .Distinct()
                .ToListAsync();

            return Ok(new UserRolesAndPermissionsDto
            {
                UserId = user.Id,
                Email = user.Email ?? string.Empty,
                FullName = user.FullName,
                Roles = roles,
                Permissions = permissions
            });
        }

        // ==========================================
        // 3. PERMISSION MANAGEMENT ENDPOINTS
        // ==========================================

        /// <summary>
        /// Retrieves all permissions in the system, grouped or listed.
        /// GET /api/admin/roles/permissions
        /// </summary>
        [HttpGet("permissions")]
        public async Task<IActionResult> GetAllPermissions()
        {
            var permissions = await _context.Permissions
                .AsNoTracking()
                .OrderBy(p => p.Category)
                .ThenBy(p => p.Name)
                .Select(p => new PermissionResponseDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Category = p.Category,
                    Description = p.Description
                })
                .ToListAsync();

            return Ok(permissions);
        }

        /// <summary>
        /// Creates a new dynamic permission.
        /// POST /api/admin/roles/permissions
        /// </summary>
        [HttpPost("permissions")]
        public async Task<IActionResult> CreatePermission([FromBody] CreatePermissionDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var trimmedName = dto.Name.Trim();
            var exists = await _context.Permissions.AnyAsync(p => p.Name.ToLower() == trimmedName.ToLower());
            if (exists)
            {
                return BadRequest(new { success = false, message = $"Permission '{trimmedName}' already exists." });
            }

            var permission = new Permission
            {
                Name = trimmedName,
                Category = dto.Category.Trim(),
                Description = dto.Description?.Trim()
            };

            _context.Permissions.Add(permission);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetAllPermissions), new { id = permission.Id }, new PermissionResponseDto
            {
                Id = permission.Id,
                Name = permission.Name,
                Category = permission.Category,
                Description = permission.Description
            });
        }

        // ==========================================
        // 4. ROLE-PERMISSION MAPPING ENDPOINTS
        // ==========================================

        /// <summary>
        /// Gets all permissions assigned to a specific role.
        /// GET /api/admin/roles/{roleId}/permissions
        /// </summary>
        [HttpGet("{roleId:guid}/permissions")]
        public async Task<IActionResult> GetRolePermissions(Guid roleId)
        {
            var role = await _roleManager.FindByIdAsync(roleId.ToString());
            if (role == null)
            {
                return NotFound(new { success = false, message = "Role not found." });
            }

            var permissions = await _context.RolePermissions
                .AsNoTracking()
                .Where(rp => rp.RoleId == roleId)
                .Select(rp => new PermissionResponseDto
                {
                    Id = rp.Permission.Id,
                    Name = rp.Permission.Name,
                    Category = rp.Permission.Category,
                    Description = rp.Permission.Description
                })
                .ToListAsync();

            return Ok(permissions);
        }

        /// <summary>
        /// Assigns / Synchronizes the list of permissions for a specific role.
        /// POST /api/admin/roles/{roleId}/permissions
        /// </summary>
        [HttpPost("{roleId:guid}/permissions")]
        public async Task<IActionResult> AssignPermissionsToRole(Guid roleId, [FromBody] AssignPermissionsToRoleDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var role = await _roleManager.FindByIdAsync(roleId.ToString());
            if (role == null)
            {
                return NotFound(new { success = false, message = "Role not found." });
            }

            // Verify all permission IDs exist
            var validPermissionIds = await _context.Permissions
                .Where(p => dto.PermissionIds.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync();

            var existingMappings = await _context.RolePermissions
                .Where(rp => rp.RoleId == roleId)
                .ToListAsync();

            // Remove existing mappings
            _context.RolePermissions.RemoveRange(existingMappings);

            // Add new mappings
            foreach (var permId in validPermissionIds)
            {
                _context.RolePermissions.Add(new RolePermission
                {
                    RoleId = roleId,
                    PermissionId = permId
                });
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Successfully updated {validPermissionIds.Count} permissions for role '{role.Name}'."
            });
        }
    }
}
