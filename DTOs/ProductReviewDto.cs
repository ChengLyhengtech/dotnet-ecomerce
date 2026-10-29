using System;
using System.ComponentModel.DataAnnotations;

namespace Ecomerce_dotnet.DTOs
{
    public class ProductReviewCreateDto
    {
        [Required]
        public Guid OrderId { get; set; }

        [Required]
        public Guid ProductId { get; set; }

        [Required]
        [Range(1, 5, ErrorMessage = "Rating score must be between 1 and 5 stars.")]
        public int Rating { get; set; }

        [MaxLength(1000, ErrorMessage = "Review comment cannot exceed 1000 characters.")]
        public string Comment { get; set; } = string.Empty;
    }

    public class ProductReviewResponseDto
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public Guid OrderId { get; set; }
        public Guid UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public int Rating { get; set; }
        public string Comment { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class ProductReviewsSummaryDto
    {
        public Guid ProductId { get; set; }
        public double AverageRating { get; set; }
        public int TotalReviews { get; set; }
        public List<ProductReviewResponseDto> Reviews { get; set; } = new();
    }
}
