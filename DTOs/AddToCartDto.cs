namespace Ecomerce_dotnet.DTOs
{
    public class AddToCartDto
    {
        public Guid UserId { get; set; }
        public Guid ProductVariantId { get; set; }
        public int Quantity { get; set; }
    }

    public class UpdateCartQuantityDto
    {
        public int Quantity { get; set; }
    }
}
