using System.ComponentModel.DataAnnotations;

namespace Ecomerce_dotnet.DTOs
{
    public class OrderCreateDto
    {
        [Required]
        public string ContactPhone { get; set; } = string.Empty;

        public string ShippingAddress { get; set; } = string.Empty;

        // Capture the map numbers directly from the frontend payload
        [Required]
        public double DeliveryLatitude { get; set; }

        [Required]
        public double DeliveryLongitude { get; set; }

        [Required]
        public List<OrderItemCreateDto> Items { get; set; } = new();
    }

    public class OrderItemCreateDto
    {
        [Required]
        public Guid VariantId { get; set; } // Targets the specific size/color variant

        [Range(1, 100)]
        public int Quantity { get; set; }
    }

    public class UpdateOrderStatusDto
    {
        [Required]
        public string Status { get; set; } = string.Empty; // e.g., "Shipped", "Delivered", "Cancelled"
    }
}
