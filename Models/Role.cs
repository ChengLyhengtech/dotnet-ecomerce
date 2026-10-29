using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;

namespace Ecomerce_dotnet.Models
{
    public class Role : IdentityRole<Guid>
    {
        public Role() : base()
        {
            Id = Guid.NewGuid();
        }

        public Role(string roleName) : base(roleName)
        {
            Id = Guid.NewGuid();
            Name = roleName;
            NormalizedName = roleName.ToUpperInvariant();
        }

        public string? Description { get; set; }

        // Navigation properties for Many-to-Many relationships
        [System.Text.Json.Serialization.JsonIgnore]
        public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

        [System.Text.Json.Serialization.JsonIgnore]
        public virtual ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    }
}
