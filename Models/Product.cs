namespace Ecomerce_dotnet.Models
{
    public class Product
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal BasePrice { get; set; }
        public decimal DiscountPercentage { get; set; }

        public Guid CategoryId { get; set; }
        public Category? Category { get; set; }

        // NEW: Brand Link (Required Foreign Key)
        public Guid BrandId { get; set; }
        public Brand Brand { get; set; } = null!;

        // Relationship: One product has many variants (Sizes/Colors)
        public List<ProductVariant> Variants { get; set; } = new();

        // Relationship: One product has many reviews
        public List<ProductReview> Reviews { get; set; } = new();

        // Target Demographic Bitwise Flags (e.g. Women | Girls)
        public TargetGender TargetGender { get; set; } = TargetGender.Unisex;

        public bool IsDeleted { get; set; } = false; // Soft delete
    }
}
