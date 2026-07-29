using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Http;

namespace Ecomerce_dotnet.DTOs
{
    public class BannerUpdateDto
    {
        public string Name { get; set; } = string.Empty;
        public string LinkUrl { get; set; } = string.Empty;
        public BannerPosition Position { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }

        // Optional image uploads: if omitted, existing image URL is preserved
        public IFormFile? DesktopImage { get; set; }
        public IFormFile? MobileImage { get; set; }
    }
}
