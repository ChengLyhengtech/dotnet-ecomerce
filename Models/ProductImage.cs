namespace Ecomerce_dotnet.Models
{
    public class ProductImage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProductId { get; set; }
        public Product? Product { get; set; }

        public string ImageUrl { get; set; } = string.Empty;
        public bool IsPrimary { get; set; } = false;

        // Direct Foreign Key link to a variant
        public Guid? ProductVariantId { get; set; }
        public ProductVariant? ProductVariant { get; set; }
    }
}
