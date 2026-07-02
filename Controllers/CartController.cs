using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CartController:ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public CartController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 1. GET: Fetch the user's single cart, including all its items and variant details
        [HttpGet("user/{userId:guid}")]
        public async Task<IActionResult> GetUserCart(Guid userId)
        {
            var cart = await _context.Carts
                .Include(c => c.Items)
                    .ThenInclude(i => i.ProductVariant) // Eagerly load variant details (price, size, etc.)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (cart == null)
            {
                // Instead of 404, professional systems return an empty cart structure
                return Ok(new { userId, items = new List<CartItem>() });
            }

            return Ok(cart);
        }

        // 2. POST: Add an item to the cart (Finds or Creates the parent Cart first)
        [HttpPost("add")]
        public async Task<IActionResult> AddToCart([FromBody] AddToCartDto dto)
        {
            if (dto.Quantity <= 0)
                return BadRequest("Quantity must be greater than 0.");

            // Step A: Find the user's active parent cart, or create it if it doesn't exist yet
            var cart = await _context.Carts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.UserId == dto.UserId);

            if (cart == null)
            {
                cart = new Cart
                {
                    UserId = dto.UserId,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Carts.Add(cart);
                await _context.SaveChangesAsync(); // Saves parent cart to generate its database Id
            }

            // Step B: Check if this specific product variant is already inside this cart
            var existingItem = cart.Items.FirstOrDefault(i => i.ProductVariantId == dto.ProductVariantId);

            if (existingItem != null)
            {
                // Variant exists, just increment the quantity count
                existingItem.Quantity += dto.Quantity;
            }
            else
            {
                // Variant doesn't exist, append a brand new CartItem entry
                var newItem = new CartItem
                {
                    CartId = cart.Id,
                    ProductVariantId = dto.ProductVariantId,
                    Quantity = dto.Quantity
                };
                cart.Items.Add(newItem);
            }

            cart.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Item successfully updated in cart." });
        }

        // 3. PUT: Directly update a specific CartItem's quantity from the quantity box input
        [HttpPut("item/{cartItemId:guid}")]
        public async Task<IActionResult> UpdateItemQuantity(Guid cartItemId, [FromBody] UpdateCartQuantityDto dto)
        {
            if (dto.Quantity <= 0)
                return BadRequest("Quantity must be at least 1. Use DELETE to remove items.");

            var cartItem = await _context.CartItems.FindAsync(cartItemId);
            if (cartItem == null) return NotFound("Cart item not found.");

            cartItem.Quantity = dto.Quantity;

            // Optional: Update parent timestamp if you want to track activity
            var parentCart = await _context.Carts.FindAsync(cartItem.CartId);
            if (parentCart != null) parentCart.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // 4. DELETE: Wipe a specific line item completely out of the cart
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
