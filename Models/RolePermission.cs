using System;
using System.Text.Json.Serialization;

namespace Ecomerce_dotnet.Models
{
    public class RolePermission
    {
        public Guid RoleId { get; set; }

        [JsonIgnore]
        public virtual Role Role { get; set; } = null!;

        public Guid PermissionId { get; set; }

        [JsonIgnore]
        public virtual Permission Permission { get; set; } = null!;
    }
}
