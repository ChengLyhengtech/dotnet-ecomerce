using System;
using System.Collections.Generic;

namespace Ecomerce_dotnet.DTOs
{
    public class UserCreateAdminDto
    {
        public required string Email { get; set; }
        public required string Password { get; set; }
        public required string FullName { get; set; }
        public required string Role { get; set; } // e.g., "Admin", "Manager"
    }

    public class UserUpdateRoleDto
    {
        public required string Role { get; set; }
    }

    public class UserBanDto
    {
        public bool IsBanned { get; set; }
        public string? BanReason { get; set; } // Nice-to-have for customer support tracking
    }

    public class AdminResetPasswordDto
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "New password is required.")]
        [System.ComponentModel.DataAnnotations.StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters long.")]
        public string NewPassword { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Compare("NewPassword", ErrorMessage = "New password and confirmation password must match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class UserResponseDto
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public bool IsBanned { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<string> Roles { get; set; } = new(); // Contains roles like ["Admin"]
    }
}
