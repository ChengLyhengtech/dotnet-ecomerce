using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace Ecomerce_dotnet.DTOs
{
    public class ShopProfileResponseDto
    {
        public Guid Id { get; set; }
        public string ShopName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? LogoUrl { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? GoogleMapUrl { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? OpeningHours { get; set; }
        public int OrderCancellationWindowHours { get; set; } = 6;
        public DateTime UpdatedAt { get; set; }
        public List<ShopContactResponseDto> Contacts { get; set; } = new List<ShopContactResponseDto>();
    }

    public class ShopProfileUpdateDto
    {
        [Required]
        [MaxLength(150)]
        public string ShopName { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        [EmailAddress]
        [MaxLength(150)]
        public string? Email { get; set; }

        [Phone]
        [MaxLength(50)]
        public string? Phone { get; set; }

        [MaxLength(300)]
        public string? Address { get; set; }

        [MaxLength(1000)]
        public string? GoogleMapUrl { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        [MaxLength(150)]
        public string? OpeningHours { get; set; }

        [Range(1, 168, ErrorMessage = "Cancellation window must be between 1 and 168 hours.")]
        public int? OrderCancellationWindowHours { get; set; }

        public IFormFile? LogoImage { get; set; }
    }

    public class ShopLocationDto
    {
        [MaxLength(300)]
        public string? Address { get; set; }

        [MaxLength(1000)]
        public string? GoogleMapUrl { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        [MaxLength(150)]
        public string? OpeningHours { get; set; }
    }
}
