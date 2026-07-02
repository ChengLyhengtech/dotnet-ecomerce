namespace Ecomerce_dotnet.Models
{
    public class Category
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }

        // Relationship: One category has many products
        public List<Product> Products { get; set; } = new();
    }
}
