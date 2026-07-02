using Ecomerce_dotnet.Models;

namespace Ecomerce_dotnet.DTOs
{
    public class BannerCreateDto
    {
        public string Name { get; set; } = string.Empty;
        public string LinkUrl { get; set; } = string.Empty;
        public BannerPosition Position { get; set; }
        public int SortOrder { get; set; }

        // Changed from string ImageUrl to IFormFile File
        // Adding 'required' fixes the 'Non-nullable property must contain a non-null value' warning
        public required IFormFile ImageFile { get; set; }
    }
}
