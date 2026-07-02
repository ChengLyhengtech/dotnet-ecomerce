namespace Ecomerce_dotnet.DTOs
{
    public class CategoryUpdateDto
    {
        public string Name { get; set; } = string.Empty;
        public IFormFile? Image { get; set; } // Optional for update
    }
}
