using System;

namespace Ecomerce_dotnet.Models
{
    public class StockMovement
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        
        public Guid ProductVariantId { get; set; }
        public ProductVariant? ProductVariant { get; set; }
        
        public int QuantityChanged { get; set; }
        
        public StockMovementType MovementType { get; set; }
        
        public string ChangedBy { get; set; } = "System";
        
        public string ReasonCode { get; set; } = string.Empty;
        
        public string? ReferenceId { get; set; }
        
        public string? Notes { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
