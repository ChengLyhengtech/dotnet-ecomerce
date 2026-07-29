using Ecomerce_dotnet.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ecomerce_dotnet.DTOs
{
    public class ProductResponseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal BasePrice { get; set; }
        public decimal DiscountPercentage { get; set; }

        // Dynamic dynamic values handled perfectly for frontend consumption
        public decimal FinalPrice => BasePrice * (1 - (DiscountPercentage / 100));
        public bool IsDiscounted => DiscountPercentage > 0;
        public int TotalAvailableStock { get; set; }

        // The dynamic field required by the frontend layout!
        public string Status => TotalAvailableStock > 0 ? "Active" : "Inactive";

        public Guid CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;

        public Guid BrandId { get; set; }
        public string BrandName { get; set; } = string.Empty;

        public TargetGender TargetGender { get; set; }
        public List<string> TargetGenders => Enum.GetValues<TargetGender>()
            .Where(g => g != TargetGender.None && TargetGender.HasFlag(g))
            .Select(g => g.ToString())
            .ToList();

        public List<ProductVariantDto> Variants { get; set; } = new();
        public List<ProductImageDto> Images { get; set; } = new();
    }

    public class ProductVariantDto
    {
        public Guid Id { get; set; }
        public string Color { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public int AvailableStock { get; set; }
        public string Sku { get; set; } = string.Empty;
    }

    public class ProductImageDto
    {
        public Guid Id { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        public string? Color { get; set; }
    }
}
