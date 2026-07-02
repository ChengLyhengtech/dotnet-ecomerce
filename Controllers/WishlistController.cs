using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Ecomerce_dotnet.Controllers
{
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
                .Include(w => w.Product) // Eagerly load product details
                .OrderByDescending(w => w.AddedAt)
                .ToListAsync();

            return Ok(wishlistItems);
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
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                throw new UnauthorizedAccessException("User is not properly authenticated.");
            }

            return userId;
        }
    }
}
