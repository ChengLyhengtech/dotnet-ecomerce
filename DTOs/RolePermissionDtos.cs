using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Ecomerce_dotnet.DTOs
{
    // Role DTOs
    public class CreateRoleDto
    {
        [Required(ErrorMessage = "Role name is required.")]
        [StringLength(50, MinimumLength = 2, ErrorMessage = "Role name must be between 2 and 50 characters.")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(250)]
        public string? Description { get; set; }
    }

    public class UpdateRoleDto
    {
        [Required(ErrorMessage = "Role name is required.")]
        [StringLength(50, MinimumLength = 2, ErrorMessage = "Role name must be between 2 and 50 characters.")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(250)]
        public string? Description { get; set; }
    }

    public class RoleResponseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<PermissionResponseDto> Permissions { get; set; } = new();
    }

    // Permission DTOs
    public class CreatePermissionDto
    {
        [Required(ErrorMessage = "Permission name is required.")]
        [StringLength(100, MinimumLength = 3, ErrorMessage = "Permission name must be between 3 and 100 characters.")]
        public string Name { get; set; } = string.Empty; // e.g., "products:create"

        [Required(ErrorMessage = "Category is required.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Category must be between 2 and 100 characters.")]
        public string Category { get; set; } = string.Empty; // e.g., "Products"

        [MaxLength(250)]
        public string? Description { get; set; }
    }

    public class PermissionResponseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    // Assignment DTOs
    public class AssignRoleToUserDto
    {
        [Required]
        public Guid UserId { get; set; }

        [Required(ErrorMessage = "Role name is required.")]
        public string RoleName { get; set; } = string.Empty;
    }

    public class RemoveRoleFromUserDto
    {
        [Required]
        public Guid UserId { get; set; }

        [Required(ErrorMessage = "Role name is required.")]
        public string RoleName { get; set; } = string.Empty;
    }

    public class AssignPermissionsToRoleDto
    {
        [Required]
        public List<Guid> PermissionIds { get; set; } = new();
    }

    public class UserRolesAndPermissionsDto
    {
        public Guid UserId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
        public List<string> Permissions { get; set; } = new();
    }
}
