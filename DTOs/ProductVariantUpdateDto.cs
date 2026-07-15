namespace Ecomerce_dotnet.DTOs
{
    public class ProductVariantUpdateDto
    {
        public string Color { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public int PhysicalQuantity { get; set; }
        public string Sku { get; set; } = string.Empty;
    }
}
