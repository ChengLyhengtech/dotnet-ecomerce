using System.Security.Claims;
using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CartController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public CartController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 1. GET: Fetch the user's single cart, including all its items, variant, product, and image details
        [HttpGet("user/{userId:guid}")]
        public async Task<IActionResult> GetUserCart(Guid userId)
        {
            var cart = await _context.Carts
                .Include(c => c.Items)
                    .ThenInclude(i => i.ProductVariant)
                        .ThenInclude(v => v!.Product)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (cart == null)
            {
                return Ok(new { userId, items = new List<object>() });
            }

            var productIds = cart.Items
                .Where(i => i.ProductVariant?.Product != null)
                .Select(i => i.ProductVariant!.ProductId)
                .Distinct()
                .ToList();

            var images = await _context.ProductImages
                .Where(img => productIds.Contains(img.ProductId))
                .ToListAsync();

            var result = new
            {
                cart.Id,
                cart.UserId,
                cart.UpdatedAt,
                Items = cart.Items
                    .Where(i => i.ProductVariant?.Product != null && !i.ProductVariant.Product.IsDeleted)
                    .Select(i => new
                    {
                        i.Id,
                        i.CartId,
                        i.ProductVariantId,
                        i.Quantity,
                        ProductVariant = i.ProductVariant == null ? null : new
                        {
                            i.ProductVariant.Id,
                            i.ProductVariant.Color,
                            i.ProductVariant.Size,
                            i.ProductVariant.Sku,
                            AvailableStock = Math.Max(0, i.ProductVariant.PhysicalQuantity - i.ProductVariant.ReservedQuantity),
                            Product = i.ProductVariant.Product == null ? null : new
                            {
                                i.ProductVariant.Product.Id,
                                i.ProductVariant.Product.Name,
                                i.ProductVariant.Product.Description,
                                i.ProductVariant.Product.BasePrice,
                                i.ProductVariant.Product.DiscountPercentage,
                                FinalPrice = i.ProductVariant.Product.BasePrice * (1 - (i.ProductVariant.Product.DiscountPercentage / 100m)),
                                Images = images.Where(img => img.ProductId == i.ProductVariant.ProductId).Select(img => new
                                {
                                    img.Id,
                                    img.ImageUrl,
                                    img.IsPrimary,
                                    img.Color
                                }).ToList()
                            }
                        }
                    })
            };

            return Ok(result);
        }

        // 2. POST: Add an item to the cart (Finds or Creates parent Cart in single atomic context save)
        [HttpPost("add")]
        public async Task<IActionResult> AddToCart([FromBody] AddToCartDto dto)
        {
            if (dto.Quantity <= 0)
                return BadRequest("Quantity must be greater than 0.");

            Guid targetUserId = dto.UserId;
            var userClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(userClaim) && Guid.TryParse(userClaim, out var claimUserId))
            {
                targetUserId = claimUserId;
            }

            if (targetUserId == Guid.Empty)
            {
                return BadRequest("Invalid or missing user context.");
            }

            // Step A: Validate Product Variant Existence & Availability
            var variant = await _context.ProductVariants
                .Include(v => v.Product)
                .FirstOrDefaultAsync(v => v.Id == dto.ProductVariantId);

            if (variant == null || variant.Product == null || variant.Product.IsDeleted)
            {
                return NotFound("The requested product variation is unavailable or discontinued.");
            }

            int availableStock = Math.Max(0, variant.PhysicalQuantity - variant.ReservedQuantity);
            if (availableStock < dto.Quantity)
            {
                return BadRequest($"Insufficient stock available. Current available stock: {availableStock}");
            }

            // Step B: Single Unit of Work Cart Resolution
            var cart = await _context.Carts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.UserId == targetUserId);

            if (cart == null)
            {
                cart = new Cart
                {
                    Id = Guid.NewGuid(),
                    UserId = targetUserId,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Carts.Add(cart);
            }
            else
            {
                cart.UpdatedAt = DateTime.UtcNow;
            }

            var existingItem = cart.Items.FirstOrDefault(i => i.ProductVariantId == dto.ProductVariantId);

            if (existingItem != null)
            {
                existingItem.Quantity += dto.Quantity;
            }
            else
            {
                var newItem = new CartItem
                {
                    Id = Guid.NewGuid(),
                    CartId = cart.Id,
                    ProductVariantId = dto.ProductVariantId,
                    Quantity = dto.Quantity
                };
                _context.CartItems.Add(newItem);
            }

            try
            {
                await _context.SaveChangesAsync();
                return Ok(new { message = "Item successfully updated in cart." });
            }
            catch (DbUpdateConcurrencyException)
            {
                // Robust Concurrency Fallback: Refresh ChangeTracker and re-apply item update
                _context.ChangeTracker.Clear();

                var freshCart = await _context.Carts
                    .Include(c => c.Items)
                    .FirstOrDefaultAsync(c => c.UserId == targetUserId);

                if (freshCart == null)
                {
                    freshCart = new Cart { UserId = targetUserId, UpdatedAt = DateTime.UtcNow };
                    _context.Carts.Add(freshCart);
                    await _context.SaveChangesAsync();
                }

                var item = freshCart.Items.FirstOrDefault(i => i.ProductVariantId == dto.ProductVariantId);
                if (item != null)
                {
                    item.Quantity += dto.Quantity;
                }
                else
                {
                    _context.CartItems.Add(new CartItem
                    {
                        CartId = freshCart.Id,
                        ProductVariantId = dto.ProductVariantId,
                        Quantity = dto.Quantity
                    });
                }

                freshCart.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return Ok(new { message = "Item successfully updated in cart." });
            }
        }

        // 3. PUT: Directly update a specific CartItem's quantity
        [HttpPut("item/{cartItemId:guid}")]
        public async Task<IActionResult> UpdateItemQuantity(Guid cartItemId, [FromBody] UpdateCartQuantityDto dto)
        {
            if (dto.Quantity <= 0)
                return BadRequest("Quantity must be at least 1. Use DELETE to remove items.");

            var cartItem = await _context.CartItems.FindAsync(cartItemId);
            if (cartItem == null) return NotFound("Cart item not found.");

            cartItem.Quantity = dto.Quantity;

            var parentCart = await _context.Carts.FindAsync(cartItem.CartId);
            if (parentCart != null) parentCart.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // 4. DELETE: Remove a specific line item completely out of the cart
        [HttpDelete("item/{cartItemId:guid}")]
        public async Task<IActionResult> RemoveFromCart(Guid cartItemId)
        {
            var cartItem = await _context.CartItems.FindAsync(cartItemId);
            if (cartItem == null) return NotFound("Cart item not found.");

            _context.CartItems.Remove(cartItem);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
