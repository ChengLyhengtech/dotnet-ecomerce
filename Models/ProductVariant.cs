using System.ComponentModel.DataAnnotations;

namespace Ecomerce_dotnet.Models
{
    public class ProductVariant
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProductId { get; set; }
        public Product? Product { get; set; }

        public string Color { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty; // e.g., "XL" or "42"

        // Real-World Stock Split
        public int PhysicalQuantity { get; set; }  // Actual count physically sitting in the warehouse
        public int ReservedQuantity { get; set; }  // Items held in active checkout/pending payment
        public decimal CostPrice { get; set; }     // Supplier unit purchase cost
        public string Sku { get; set; } = string.Empty; // Stock Keeping Unit (Unique ID)

        // Computed property: What is actually buyable right now
        public int AvailableStock => PhysicalQuantity - ReservedQuantity;

        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
}
