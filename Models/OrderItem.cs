namespace Ecomerce_dotnet.Models
{
    public class OrderItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid OrderId { get; set; }
        public Order? Order { get; set; }

        public Guid VariantId { get; set; }
        public ProductVariant? ProductVariant { get; set; }

        public int Quantity { get; set; }
        public decimal PriceAtPurchase { get; set; } // Snapshot price
        public decimal UnitCost { get; set; } // Snapshot supplier unit purchase cost
    }
}
