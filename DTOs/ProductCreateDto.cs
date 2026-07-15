using Ecomerce_dotnet.Binder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Ecomerce_dotnet.DTOs
{
    public class ProductCreateDto
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
        [Required]
        public Guid BrandId { get; set; }
        // Explicitly intercept the form field and process it as JSON data
        [ModelBinder(BinderType = typeof(JsonModelBinder))]
        public List<VariantCreateDto> Variants { get; set; } = new();

        // Swagger-friendly approach for file uploads:
        // Swagger UI only renders file pickers for top-level IFormFile properties.
        public List<IFormFile>? Images { get; set; }

        // Parallel arrays for metadata matching by index
        public List<string>? ImageTargetSkus { get; set; }
        public List<bool>? ImageIsPrimary { get; set; }
    }
}
