using System;
using System.Collections.Generic;

namespace Ecomerce_dotnet.Models
{
    public class Category
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }

        // Self-referencing Parent-Child Hierarchy
        public Guid? ParentCategoryId { get; set; }
        public Category? ParentCategory { get; set; }
        public ICollection<Category> SubCategories { get; set; } = new List<Category>();

        // Relationship: One category has many products
        public List<Product> Products { get; set; } = new List<Product>();
    }
}
