namespace Ecomerce_dotnet.Models
{
    public class ProductImage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProductId { get; set; }
        public Product? Product { get; set; }

        public string ImageUrl { get; set; } = string.Empty;
        public bool IsPrimary { get; set; } = false;

        // When Color is set (e.g., "brown", "black"), these images represent that specific color variation across all sizes.
        // If Color is null, the image represents a general product hero photo.
        public string? Color { get; set; }
    }
}
