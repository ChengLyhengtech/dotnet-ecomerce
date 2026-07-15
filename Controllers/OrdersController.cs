using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Models;
using Ecomerce_dotnet.Services;

namespace Ecomerce_dotnet.Controllers
{
    //[Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly BakongQrService _qrService;
        private readonly IConfiguration _config;

        public OrdersController(ApplicationDbContext context, BakongQrService qrService, IConfiguration config)
        {
            _context = context;
            _qrService = qrService;
            _config = config;
        }

        // POST: api/orders (Place Pending Order & Output Processing QR Metadata)
        [HttpPost]
        public async Task<IActionResult> CreateOrder([FromBody] OrderCreateDto dto)
        {
            if (dto.Items == null || !dto.Items.Any())
            {
                return BadRequest("Your shopping cart cannot be empty.");
            }

            var userId = GetUserId();
            decimal calculatedTotalAmount = 0;

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var order = new Order
                {
                    UserId = userId,
                    ContactPhone = dto.ContactPhone,
                    ShippingAddress = dto.ShippingAddress,
                    DeliveryLatitude = dto.DeliveryLatitude,
                    DeliveryLongitude = dto.DeliveryLongitude,
                    Status = "Pending" // Starts locked in placeholder status
                };

                _context.Orders.Add(order);

                foreach (var itemDto in dto.Items)
                {
                    var variant = await _context.ProductVariants
                        .Include(v => v.Product)
                        .FirstOrDefaultAsync(v => v.Id == itemDto.VariantId);

                    if (variant == null || variant.Product == null || variant.Product.IsDeleted)
                    {
                        return NotFound($"The product variation with ID {itemDto.VariantId} is unavailable.");
                    }

                    int currentAvailableStock = variant.PhysicalQuantity - variant.ReservedQuantity;
                    if (currentAvailableStock < itemDto.Quantity)
                    {
                        return BadRequest($"Insufficient inventory stock for SKU '{variant.Sku}'. Available stock: {currentAvailableStock}");
                    }

                    decimal finalProductPrice = variant.Product.BasePrice * (1 - (variant.Product.DiscountPercentage / 100));
                    calculatedTotalAmount += finalProductPrice * itemDto.Quantity;

                    var orderItem = new OrderItem
                    {
                        OrderId = order.Id,
                        VariantId = variant.Id,
                        Quantity = itemDto.Quantity,
                        PriceAtPurchase = finalProductPrice
                    };

                    // Guard warehouse stock by putting requested counts into reservation hold
                    variant.ReservedQuantity += itemDto.Quantity;
                    _context.OrderItems.Add(orderItem);
                }

                order.TotalAmount = calculatedTotalAmount;

                // --- Generate Bakong KHQR Strings Immediately ---
                var accountName = _config["BakongSettings:AccountName"] ?? "";
                var username = _config["BakongSettings:AccountUsername"] ?? "";
                var expirationTime = DateTime.UtcNow.AddMinutes(15); // Sync with your background cleanup timeout worker

                string qrString = _qrService.GenerateIndividualQrString(username, accountName, "PHNOM PENH", order.TotalAmount, "USD");
                string md5Hash = _qrService.ComputeMd5Hash(qrString);

                order.QrCode = qrString;
                order.QrMd5 = md5Hash;
                order.QrExpiration = expirationTime;
                order.PaymentMethod = "khqr";

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Return everything the frontend needs to render the map tracking dashboard and the payment QR modal
                return Ok(new
                {
                    success = true,
                    message = "Order placed and stock reserved under pending verification.",
                    data = new
                    {
                        orderId = order.Id,
                        merchant_name = accountName,
                        qr_code = order.QrCode,
                        qr_md5 = order.QrMd5,
                        amount = order.TotalAmount,
                        currency = "USD",
                        qr_expiration = order.QrExpiration.Value.ToString("o")
                    }
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"An error occurred during transaction checkout handling: {ex.Message}");
            }
        }

        private Guid GetUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                throw new UnauthorizedAccessException("User context is invalid or token missing.");
            }
            return userId;
        }
        // ========================================================
        // 2. GET: api/orders (Advanced Paginated Admin Dashboard Order History)
        // ========================================================
        [HttpGet]
 
        public async Task<IActionResult> GetOrderHistory([FromQuery] OrderQueryDto queryDto)
        {
            // Enforce safe boundaries on window windows
            if (queryDto.PageNumber < 1) queryDto.PageNumber = 1;
            if (queryDto.PageSize < 1 || queryDto.PageSize > 50) queryDto.PageSize = 10;

            // 1. Initialize query context including relational User mapping to get names/emails
            var query = _context.Orders
                .Include(o => o.User)
                .AsQueryable();

            // 2. APPLY STATUS FILTERING
            if (!string.IsNullOrWhiteSpace(queryDto.Status))
            {
                query = query.Where(o => o.Status.ToLower() == queryDto.Status.ToLower());
            }
            else
            {
                // Professional Rule: Default view excludes unpaid 'Pending' abandonments
                query = query.Where(o => o.Status.ToLower() != "pending");
            }

            // 3. APPLY MULTI-KEY SEARCH TERM EXTENSIONS
            if (!string.IsNullOrWhiteSpace(queryDto.SearchTerm))
            {
                var term = queryDto.SearchTerm.Trim().ToLower();

                // If the user types a full or partial Guid tracking string
                Guid.TryParse(term, out Guid parsedOrderId);

                query = query.Where(o =>
                    o.Id == parsedOrderId ||
                    o.ContactPhone.Contains(term) ||
                    o.ShippingAddress.ToLower().Contains(term) ||
                    o.User.FullName.ToLower().Contains(term) ||
                    o.User.Email.ToLower().Contains(term)
                );
            }

            // 4. APPLY DYNAMIC ORDER SORTING
            if (!string.IsNullOrWhiteSpace(queryDto.SortBy) && queryDto.SortBy.ToLower() == "amount")
            {
                query = queryDto.IsDescending
                    ? query.OrderByDescending(o => o.TotalAmount)
                    : query.OrderBy(o => o.TotalAmount);
            }
            else
            {
                // Default: Sort by date
                query = queryDto.IsDescending
                    ? query.OrderByDescending(o => o.OrderDate)
                    : query.OrderBy(o => o.OrderDate);
            }

            // 5. CAPTURE PAGINATED MATRIX COUNTS
            int totalCount = await query.CountAsync();

            // 6. EXECUTE HIGH-PERFORMANCE DATABASE WINDOW SLICE
            var orders = await query
                .Skip((queryDto.PageNumber - 1) * queryDto.PageSize)
                .Take(queryDto.PageSize)
                .Select(o => new OrderHistoryResponseDto
                {
                    Id = o.Id,
                    CustomerName = o.User.FullName ?? "Guest Customer",
                    CustomerEmail = o.User.Email ?? "N/A",
                    OrderDate = o.OrderDate,
                    TotalAmount = o.TotalAmount,
                    Status = o.Status,
                    ContactPhone = o.ContactPhone,
                    ShippingAddress = o.ShippingAddress,
                    Latitude = o.DeliveryLatitude,
                    Longitude = o.DeliveryLongitude
                })
                .ToListAsync();

            // 7. PACKAGE WRAPPED PAGINATION OBJECT
            var result = new PagedResult<OrderHistoryResponseDto>
            {
                Items = orders,
                PageNumber = queryDto.PageNumber,
                PageSize = queryDto.PageSize,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling(totalCount / (double)queryDto.PageSize)
            };

            return Ok(result);
        }

        // 3. GET: api/orders/{id} (Detailed Order Overview Card)
        [HttpGet("{id}")]
        public async Task<IActionResult> GetOrderById(Guid id)
        {
            var order = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.Payment)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.ProductVariant)
                        .ThenInclude(v => v!.Product)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound("The requested order records could not be found.");

            var response = new
            {
                order.Id,
                order.OrderDate,
                order.TotalAmount,
                order.Status,
                order.ContactPhone,
                order.ShippingAddress,
                Latitude = order.DeliveryLatitude,
                Longitude = order.DeliveryLongitude,
                Customer = new
                {
                    order.User?.Id,
                    order.User?.FullName,
                    order.User?.Email
                },
                PaymentDetails = order.Payment == null ? null : new
                {
                    order.Payment.BakongTransactionRef,
                    order.Payment.Status,
                    order.Payment.PaidAt
                },
                Items = order.OrderItems.Select(oi => new
                {
                    oi.Id,
                    ProductName = oi.ProductVariant?.Product?.Name ?? "Unknown Product",
                    Size = oi.ProductVariant?.Size,
                    Color = oi.ProductVariant?.Color,
                    Sku = oi.ProductVariant?.Sku,
                    oi.Quantity,
                    oi.PriceAtPurchase
                })
            };

            return Ok(response);
        }

        // 4. PUT: api/orders/{id}/status (Update Order Tracking to Shipped / Delivered / Finished)
        [HttpPut("{id}/status")]
        // [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateOrderStatusDto dto)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound("Order record not found.");

            // Prevent manual tampering of unpaid orders through the status endpoint
            if (order.Status == "Pending")
            {
                return BadRequest("Cannot manually transition the status of an unverified checkout session.");
            }

            // Standard string tracking assignment matching your database rules
            order.Status = dto.Status; // Accepts string parameters like "Shipped", "Delivered", or "Finished"

            await _context.SaveChangesAsync();

            return Ok(new { message = $"Order state transitioned to '{dto.Status}' successfully." });
        }
    }
}