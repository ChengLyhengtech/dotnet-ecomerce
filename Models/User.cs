using Microsoft.AspNetCore.Identity;

namespace Ecomerce_dotnet.Models
{
    public class User : IdentityUser<Guid>
    {
        public string FullName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsBanned { get; set; } = false;

        // Refresh Token Persistence
        public string? RefreshToken { get; set; }
        public DateTime? RefreshTokenExpiryTime { get; set; }

        // Relationship
        public List<Order> Orders { get; set; } = new();
    }
}
