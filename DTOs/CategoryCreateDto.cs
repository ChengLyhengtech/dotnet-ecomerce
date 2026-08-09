using Microsoft.AspNetCore.Http;
using System;
using System.ComponentModel.DataAnnotations;

namespace Ecomerce_dotnet.DTOs
{
    public class CategoryCreateDto
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public IFormFile? Image { get; set; }

        public Guid? ParentCategoryId { get; set; }
    }
}
