namespace Ecomerce_dotnet.Models
{
    public class Order
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public User? User { get; set; }

        public DateTime OrderDate { get; set; } = DateTime.UtcNow;
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = "Pending"; // Pending, Paid, Shipped, Cancelled
        public string ShippingAddress { get; set; } = string.Empty;
        public string ContactPhone { get; set; } = string.Empty;

        public double DeliveryLatitude { get; set; }
        public double DeliveryLongitude { get; set; }

        // --- Added Bakong KHQR Temporary Tracking Fields ---
        public string? QrCode { get; set; }
        public string? QrMd5 { get; set; }
        public DateTime? QrExpiration { get; set; }
        public string? PaymentMethod { get; set; } // e.g., "khqr"

        // Relationships
        public List<OrderItem> OrderItems { get; set; } = new();
        public Payment? Payment { get; set; }
    }
}
