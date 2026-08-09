using System;

namespace Ecomerce_dotnet.Models
{
    public class ShopProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string ShopName { get; set; } = "My E-Commerce Store";
        public string? Description { get; set; }
        public string? LogoUrl { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? GoogleMapUrl { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? OpeningHours { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
