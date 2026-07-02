namespace Ecomerce_dotnet.Models
{
    public class Cart
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; } // Foreign key to User table
        public User? User { get; set; }

        public List<CartItem> Items { get; set; } = new();
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class CartItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CartId { get; set; }
        public Cart? Cart { get; set; }

        // CRITICAL: Point to the variant, so you know exactly which size/color is in the cart
        public Guid ProductVariantId { get; set; }
        public ProductVariant? ProductVariant { get; set; }

        public int Quantity { get; set; }
    }
}
