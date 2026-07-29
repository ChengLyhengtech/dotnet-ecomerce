using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Ecomerce_dotnet.DTOs
{
    public class ColorGroupCreateDto
    {
        [Required(ErrorMessage = "Color is required.")]
        public string Color { get; set; } = string.Empty;

        public List<SizeStockCreateDto> Sizes { get; set; } = new();
    }
}
