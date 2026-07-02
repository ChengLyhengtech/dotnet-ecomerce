namespace Ecomerce_dotnet.DTOs
{
    public class ProductResponseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal BasePrice { get; set; }
        public decimal DiscountPercentage { get; set; }

        // Dynamic dynamic values handled perfectly for frontend consumption
        public decimal FinalPrice => BasePrice * (1 - (DiscountPercentage / 100));
        public bool IsDiscounted => DiscountPercentage > 0;
        public int TotalAvailableStock { get; set; }

        public Guid CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;

        public List<ProductVariantDto> Variants { get; set; } = new();
        public List<ProductImageDto> Images { get; set; } = new();
    }

    public class ProductVariantDto
    {
        public Guid Id { get; set; }
        public string Color { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public int AvailableStock { get; set; }
        public string Sku { get; set; } = string.Empty;
    }

    public class ProductImageDto
    {
        public Guid Id { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        public Guid? ProductVariantId { get; set; }
    }
}
