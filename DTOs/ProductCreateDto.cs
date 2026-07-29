using Ecomerce_dotnet.Binder;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
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

        public TargetGender TargetGender { get; set; } = TargetGender.Unisex;

        // JSON payload describing color variations and nested size matrix
        [ModelBinder(BinderType = typeof(JsonModelBinder))]
        public List<ColorGroupCreateDto> ColorGroups { get; set; } = new();

        // Top-level file uploads mapped dynamically by color
        public List<IFormFile>? Images { get; set; }

        // Parallel arrays for metadata matching by index
        public List<string>? ImageColors { get; set; } // Matches image index to color string
        public List<bool>? ImageIsPrimary { get; set; }

        // Optional legacy fallback if flat variants array is passed
        [ModelBinder(BinderType = typeof(JsonModelBinder))]
        public List<VariantCreateDto>? Variants { get; set; }
    }
}
