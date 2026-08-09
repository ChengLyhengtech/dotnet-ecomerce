using System;

namespace Ecomerce_dotnet.DTOs
{
    public class StockAdjustmentRequestDto
    {
        public Guid VariantId { get; set; }
        
        public int Quantity { get; set; }
        
        public decimal UnitCost { get; set; }
        
        public string ReasonCode { get; set; } = string.Empty;
        
        public string? ReferenceId { get; set; }
        
        public string? Notes { get; set; }
    }

    public class ReturnRequestCreateDto
    {
        public Guid OrderId { get; set; }
        
        public Guid VariantId { get; set; }
        
        public int Quantity { get; set; }
        
        public string CustomerReason { get; set; } = string.Empty;
        
        public string? CustomerNotes { get; set; }
    }

    public class ReturnRequestReviewDto
    {
        public string Status { get; set; } = string.Empty; // "Approved" or "Rejected"
        
        public string? AdminNotes { get; set; }
        
        public bool IsRestockable { get; set; }
    }

    public class StockMovementHistoryDto
    {
        public Guid Id { get; set; }
        public Guid ProductVariantId { get; set; }
        public int QuantityChanged { get; set; }
        public decimal UnitCost { get; set; }
        public string MovementType { get; set; } = string.Empty;
        public string ChangedBy { get; set; } = string.Empty;
        public string ReasonCode { get; set; } = string.Empty;
        public string? ReferenceId { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ReturnRequestResponseDto
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public Guid ProductVariantId { get; set; }
        public Guid? ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string CustomerReason { get; set; } = string.Empty;
        public string? CustomerNotes { get; set; }
        public string? AdminNotes { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
