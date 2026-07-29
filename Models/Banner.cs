namespace Ecomerce_dotnet.Models
{
    public enum BannerPosition
    {
        HomePageHero = 1,       // Main carousel on the home page
        AllProductsTop = 2,     // Wide banner at the top of the products listing
        CartPageBottom = 3,     // Promotional banner under the shopping cart
        PromotionPopup = 4      // A popup banner for special events
    }

    public class Banner
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // Highly recommended for admin tracking (e.g., "Black Friday Hero")
        public string Name { get; set; } = string.Empty;

        // Wide resolution image URL for Desktop/Laptop viewports (e.g., 1920x600)
        public string DesktopImageUrl { get; set; } = string.Empty;

        // Portrait/Square resolution image URL optimized for Mobile viewports (e.g., 800x1000)
        public string MobileImageUrl { get; set; } = string.Empty;

        // Backward compatibility fallback property for legacy API consumers
        public string ImageUrl => DesktopImageUrl;

        // Where the user goes when they click the banner (e.g., "/products?category=shoes")
        public string LinkUrl { get; set; } = string.Empty;

        // Determines where it displays
        public BannerPosition Position { get; set; }

        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
