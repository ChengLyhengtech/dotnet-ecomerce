using System;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;

namespace Ecomerce_dotnet.Models
{
    public class UserRole : IdentityUserRole<Guid>
    {
        // Navigation properties
        [JsonIgnore]
        public virtual User User { get; set; } = null!;

        [JsonIgnore]
        public virtual Role Role { get; set; } = null!;
    }
}
