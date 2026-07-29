using Microsoft.AspNetCore.Http;

namespace Ecomerce_dotnet.DTOs
{
    public class ProductImageUploadDto
    {
        // The raw file uploaded from the user's computer/phone
        public IFormFile File { get; set; } = null!;
        public bool IsPrimary { get; set; } = false;
        public string? Color { get; set; }
    }
}
