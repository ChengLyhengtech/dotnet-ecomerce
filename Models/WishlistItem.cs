namespace Ecomerce_dotnet.Models
{
    public class WishlistItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        public User? User { get; set; }

        // Note: Link to ProductId here, NOT ProductVariantId. 
        // Users usually favorite the overall item, then choose their size later when adding to the cart!
        public Guid ProductId { get; set; }
        public Product? Product { get; set; }

        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    }
}
