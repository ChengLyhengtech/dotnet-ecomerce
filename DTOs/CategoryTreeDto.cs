using System;
using System.Collections.Generic;

namespace Ecomerce_dotnet.DTOs
{
    public class CategoryTreeDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public Guid? ParentCategoryId { get; set; }
        public int DirectProductCount { get; set; }
        public int TotalProductCount { get; set; } // Direct products + all nested subcategories products
        public string Status => DirectProductCount > 0 || TotalProductCount > 0 ? "Active" : "Inactive";
        public List<CategoryTreeDto> SubCategories { get; set; } = new List<CategoryTreeDto>();
    }
}
