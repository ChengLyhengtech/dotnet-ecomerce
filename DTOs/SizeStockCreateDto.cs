using System.ComponentModel.DataAnnotations;

namespace Ecomerce_dotnet.DTOs
{
    public class SizeStockCreateDto
    {
        [Required(ErrorMessage = "Size is required.")]
        public string Size { get; set; } = string.Empty;

        [Required]
        [Range(0, 100000, ErrorMessage = "Physical quantity must be between 0 and 100,000.")]
        public int PhysicalQuantity { get; set; }

        public string? Sku { get; set; }
    }
}
