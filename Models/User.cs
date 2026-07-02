using Microsoft.AspNetCore.Identity;

namespace Ecomerce_dotnet.Models
{
    public class User : IdentityUser<Guid>
    {
        public string FullName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // 2. Add this missing property to fix the controller error!
        public bool IsBanned { get; set; } = false;

        // Relationship
        public List<Order> Orders { get; set; } = new();
    }
}
