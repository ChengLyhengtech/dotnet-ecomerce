using System;

namespace Ecomerce_dotnet.Models
{
    public class ReturnRequest
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        
        public Guid OrderId { get; set; }
        public Order? Order { get; set; }
        
        public Guid ProductVariantId { get; set; }
        public ProductVariant? ProductVariant { get; set; }
        
        public int Quantity { get; set; }
        
        public string CustomerReason { get; set; } = string.Empty;
        
        public string? CustomerNotes { get; set; }
        
        public string? AdminNotes { get; set; }
        
        public ReturnRequestStatus Status { get; set; } = ReturnRequestStatus.PendingReview;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
