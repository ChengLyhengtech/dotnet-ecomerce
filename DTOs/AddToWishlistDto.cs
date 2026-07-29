using System;

namespace Ecomerce_dotnet.DTOs
{
    public class AddToWishlistDto
    {
        public Guid ProductId { get; set; }
    }

    public class WishlistItemResponseDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid ProductId { get; set; }
        public DateTime AddedAt { get; set; }
        public ProductResponseDto Product { get; set; } = null!;
    }
}
