using System.ComponentModel.DataAnnotations;

namespace Ecomerce_dotnet.DTOs
{
    public class BrandCreateDto
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public IFormFile LogoImage { get; set; } = null!;
    }

    public class BrandUpdateDto
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public IFormFile? LogoImage { get; set; } // Optional in edit mode
    }
}
