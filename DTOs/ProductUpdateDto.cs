using System.ComponentModel.DataAnnotations;

namespace Ecomerce_dotnet.DTOs
{
    public class ProductUpdateDto
    {
        [Required]
        [StringLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string Description { get; set; } = string.Empty;

        [Range(0.01, 1000000)]
        public decimal BasePrice { get; set; }

        [Range(0, 100)]
        public decimal DiscountPercentage { get; set; }

        [Required]
        public Guid CategoryId { get; set; }
    }
}
