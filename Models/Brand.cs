namespace Ecomerce_dotnet.Models
{
    public class Brand
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? LogoUrl { get; set; } // For rendering brand logos on the UI

        // Navigation property for EF Core
        public ICollection<Product> Products { get; set; } = new List<Product>();
    }
}
