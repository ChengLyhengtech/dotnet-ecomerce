using System;

namespace Ecomerce_dotnet.Models
{
    public class ShopContact
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty; // e.g. Facebook, TikTok, Telegram, Phone
        public string ProfileUrl { get; set; } = string.Empty; // URL or handle/phone
        public string IconUrl { get; set; } = string.Empty; // Path to uploaded icon image
        public string ContactType { get; set; } = "Social"; // Social, Phone, Email, Messenger, Other
        public int DisplayOrder { get; set; } = 0;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
