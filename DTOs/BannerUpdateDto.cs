using Ecomerce_dotnet.Models;

namespace Ecomerce_dotnet.DTOs
{
    public class BannerUpdateDto
    {
        public string Name { get; set; } = string.Empty;
        public string LinkUrl { get; set; } = string.Empty;
        public BannerPosition Position { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public IFormFile? ImageFile { get; set; } // Optional if they don't want to change the image
    }
}
