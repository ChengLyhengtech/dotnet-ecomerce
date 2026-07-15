namespace Ecomerce_dotnet.DTOs
{
    public class ProductImageUploadDto
    {
        // The actual raw file uploaded from the user's computer/phone
        public IFormFile File { get; set; } = null!;
        public bool IsPrimary { get; set; } = false;
        public Guid? ProductVariantId { get; set; }

        public string? TargetSku { get; set; }
    }
}
