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
    public class UserResponseDto
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsBanned { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<string> Roles { get; set; } = new(); // Contains roles like ["Admin"]
    }
}
