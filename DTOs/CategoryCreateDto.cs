namespace Ecomerce_dotnet.DTOs
{
    public class CategoryCreateDto
    {
        public string Name { get; set; } = string.Empty;
        public IFormFile Image { get; set; } = null!;
    }
}
