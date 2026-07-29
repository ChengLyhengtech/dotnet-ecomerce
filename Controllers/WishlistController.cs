using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Ecomerce_dotnet.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class WishlistController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public WishlistController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 1. GET: api/wishlist
        [HttpGet]
        public async Task<IActionResult> GetWishlist()
        {
            var userId = GetUserId();

            var wishlistItems = await _context.WishlistItems
                .Where(w => w.UserId == userId)
                .Include(w => w.Product)
                    .ThenInclude(p => p!.Category)
                .Include(w => w.Product)
                    .ThenInclude(p => p!.Brand)
                .Include(w => w.Product)
                    .ThenInclude(p => p!.Variants)
                .OrderByDescending(w => w.AddedAt)
                .ToListAsync();

            var productIds = wishlistItems
                .Where(w => w.Product != null && !w.Product.IsDeleted)
                .Select(w => w.ProductId)
                .Distinct()
                .ToList();

            var images = await _context.ProductImages
                .Where(img => productIds.Contains(img.ProductId))
                .ToListAsync();

            var response = wishlistItems
                .Where(w => w.Product != null && !w.Product.IsDeleted)
                .Select(w => new WishlistItemResponseDto
                {
                    Id = w.Id,
                    UserId = w.UserId,
                    ProductId = w.ProductId,
                    AddedAt = w.AddedAt,
                    Product = new ProductResponseDto
                    {
                        Id = w.Product!.Id,
                        Name = w.Product.Name,
                        Description = w.Product.Description,
                        BasePrice = w.Product.BasePrice,
                        DiscountPercentage = w.Product.DiscountPercentage,
                        CategoryId = w.Product.CategoryId,
                        CategoryName = w.Product.Category?.Name ?? "Unknown",
                        BrandId = w.Product.BrandId,
                        BrandName = w.Product.Brand?.Name ?? "Unknown",
                        TargetGender = w.Product.TargetGender,
                        TotalAvailableStock = w.Product.Variants.Sum(v => v.PhysicalQuantity - v.ReservedQuantity),
                        Variants = w.Product.Variants.Select(v => new ProductVariantDto
                        {
                            Id = v.Id,
                            Color = v.Color,
                            Size = v.Size,
                            AvailableStock = Math.Max(0, v.PhysicalQuantity - v.ReservedQuantity),
                            Sku = v.Sku
                        }).ToList(),
                        Images = images.Where(img => img.ProductId == w.ProductId).Select(img => new ProductImageDto
                        {
                            Id = img.Id,
                            ImageUrl = img.ImageUrl,
                            IsPrimary = img.IsPrimary,
                            Color = img.Color
                        }).ToList()
                    }
                })
                .ToList();

            return Ok(response);
        }

        // 2. POST: api/wishlist
        [HttpPost]
        public async Task<IActionResult> AddToWishlist([FromBody] AddToWishlistDto dto)
        {
            var userId = GetUserId();

            // Verify the product actually exists before favoriting it
            var productExists = await _context.Products.AnyAsync(p => p.Id == dto.ProductId);
            if (!productExists)
            {
                return NotFound("Product not found.");
            }

            // Prevent duplicate entries for the same product
            var alreadyExists = await _context.WishlistItems
                .AnyAsync(w => w.UserId == userId && w.ProductId == dto.ProductId);

            if (alreadyExists)
            {
                return BadRequest("Product is already in your wishlist.");
            }

            var wishlistItem = new WishlistItem
            {
                UserId = userId,
                ProductId = dto.ProductId
            };

            _context.WishlistItems.Add(wishlistItem);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Product added to wishlist successfully." });
        }

        // 3. DELETE: api/wishlist/{productId}
        // Tip: Deleting by ProductId is usually easier for the frontend than passing the WishlistItem Guid!
        [HttpDelete("{productId}")]
        public async Task<IActionResult> RemoveFromWishlist(Guid productId)
        {
            var userId = GetUserId();

            var wishlistItem = await _context.WishlistItems
                .FirstOrDefaultAsync(w => w.UserId == userId && w.ProductId == productId);

            if (wishlistItem == null)
            {
                return NotFound("Item not found in your wishlist.");
            }

            _context.WishlistItems.Remove(wishlistItem);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Product removed from wishlist successfully." });
        }

        // Helper method to extract the logged-in User's ID safely
        private Guid GetUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst("id")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                throw new UnauthorizedAccessException("User is not properly authenticated or token missing NameIdentifier claim.");
            }

            return userId;
        }
    }
}
