namespace Ecomerce_dotnet.DTOs
{
    public class CategoryResponseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public int ProductCount { get; set; } // The new dynamic field

        // Dynamic backend logic based on inventory presence
        public string Status => ProductCount > 0 ? "Active" : "Inactive";
    }
}
