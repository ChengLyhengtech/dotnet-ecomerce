using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;

namespace Ecomerce_dotnet.Models
{
    public class User : IdentityUser<Guid>
    {
        public string FullName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsBanned { get; set; } = false;
        public bool MustChangePassword { get; set; } = false;

        // Refresh Token Persistence
        public string? RefreshToken { get; set; }
        public DateTime? RefreshTokenExpiryTime { get; set; }

        // Optional User Avatar
        public string? AvatarUrl { get; set; }

        [NotMapped]
        public string? Avatar
        {
            get => AvatarUrl;
            set => AvatarUrl = value;
        }

        // Relationship
        [JsonIgnore]
        public List<Order> Orders { get; set; } = new();

        [JsonIgnore]
        public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    }
}
