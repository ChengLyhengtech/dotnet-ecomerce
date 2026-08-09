using System.ComponentModel.DataAnnotations;

namespace Ecomerce_dotnet.DTOs
{
    public class VariantCreateDto
    {
        [Required]
        public string Color { get; set; } = string.Empty;

        [Required]
        public string Size { get; set; } = string.Empty;

        [Range(0, 100000)]
        public int InitialPhysicalQuantity { get; set; }

        [Range(0, 1000000)]
        public decimal UnitCost { get; set; }

        [Required]
        public string Sku { get; set; } = string.Empty;
    }
}
