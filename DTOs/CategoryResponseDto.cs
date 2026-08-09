using System;
using System.Collections.Generic;

namespace Ecomerce_dotnet.DTOs
{
    public class CategoryResponseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public Guid? ParentCategoryId { get; set; }
        public string? ParentCategoryName { get; set; }
        public int ProductCount { get; set; }
        public int SubCategoryCount { get; set; }
        public string Status => ProductCount > 0 ? "Active" : "Inactive";
        public List<CategoryResponseDto>? SubCategories { get; set; }
    }
}
