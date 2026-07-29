using System.ComponentModel.DataAnnotations;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Http;

namespace Ecomerce_dotnet.DTOs
{
    public class BannerCreateDto
    {
        [Required(ErrorMessage = "Banner name is required.")]
        public string Name { get; set; } = string.Empty;

        public string LinkUrl { get; set; } = string.Empty;

        [Required(ErrorMessage = "Banner position is required.")]
        public BannerPosition Position { get; set; }

        public int SortOrder { get; set; }

        // Required desktop high-res wide image file (e.g., 1920x600)
        [Required(ErrorMessage = "Desktop banner image file is required.")]
        public required IFormFile DesktopImage { get; set; }

        // Required mobile phone portrait/square image file (e.g., 800x1000)
        [Required(ErrorMessage = "Mobile banner image file is required.")]
        public required IFormFile MobileImage { get; set; }
    }
}
