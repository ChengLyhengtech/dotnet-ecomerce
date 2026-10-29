using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ecomerce_dotnet.Models
{
    public class Permission
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty; // e.g. "products:create", "orders:review"

        [Required]
        [MaxLength(100)]
        public string Category { get; set; } = string.Empty; // e.g. "Products", "Orders", "Users"

        [MaxLength(250)]
        public string? Description { get; set; }

        // Navigation property for RolePermission junction
        [JsonIgnore]
        public virtual ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    }
}
