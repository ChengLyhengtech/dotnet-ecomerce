using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace Ecomerce_dotnet.DTOs
{
    public class ShopContactResponseDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ProfileUrl { get; set; } = string.Empty;
        public string IconUrl { get; set; } = string.Empty;
        public string ContactType { get; set; } = "Social";
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ShopContactCreateDto
    {
        [Required]
        [MaxLength(100)]
        public string Title { get; set; } = string.Empty; // e.g. "Facebook", "TikTok", "Telegram", "Phone"

        [Required]
        [MaxLength(500)]
        public string ProfileUrl { get; set; } = string.Empty; // e.g. "https://facebook.com/myshop"

        [Required]
        public IFormFile IconImage { get; set; } = null!;

        [MaxLength(50)]
        public string ContactType { get; set; } = "Social"; // Social, Phone, Email, Messenger, Other

        public int DisplayOrder { get; set; } = 0;

        public bool IsActive { get; set; } = true;
    }

    public class ShopContactUpdateDto
    {
        [Required]
        [MaxLength(100)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string ProfileUrl { get; set; } = string.Empty;

        public IFormFile? IconImage { get; set; }

        [MaxLength(50)]
        public string ContactType { get; set; } = "Social";

        public int DisplayOrder { get; set; } = 0;

        public bool IsActive { get; set; } = true;
    }
}
