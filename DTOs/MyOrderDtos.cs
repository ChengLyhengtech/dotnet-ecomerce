using System;
using System.Collections.Generic;

namespace Ecomerce_dotnet.DTOs
{
    public class MyOrdersQueryDto
    {
        public string? Status { get; set; }
        public string? Search { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }

    public class MyOrderResponseDto
    {
        public Guid Id { get; set; }
        public DateTime OrderDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string ShippingAddress { get; set; } = string.Empty;
        public string ContactPhone { get; set; } = string.Empty;
        public double DeliveryLatitude { get; set; }
        public double DeliveryLongitude { get; set; }
        public bool CanCancel { get; set; }
        public int CancellationWindowHours { get; set; }
        public DateTime? CancellationCutoffTime { get; set; }
        public List<MyOrderItemResponseDto> Items { get; set; } = new();
    }

    public class MyOrderItemResponseDto
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public Guid VariantId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? Sku { get; set; }
        public string? Color { get; set; }
        public string? Size { get; set; }
        public string ImageUrl { get; set; } = string.Empty; // Exactly one image URL
        public int Quantity { get; set; }
        public decimal PriceAtPurchase { get; set; }
        public decimal SubTotal { get; set; }
    }
}
