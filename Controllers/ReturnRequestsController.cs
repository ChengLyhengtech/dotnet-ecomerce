using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Models;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReturnRequestsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IDistributedCache _cache;
        private readonly ILogger<ReturnRequestsController> _logger;

        public ReturnRequestsController(ApplicationDbContext context, IDistributedCache cache, ILogger<ReturnRequestsController> logger)
        {
            _context = context;
            _cache = cache;
            _logger = logger;
        }

        // POST: api/ReturnRequests/request
        [HttpPost("request")]
        //[Authorize]
        public async Task<IActionResult> CreateRequest([FromBody] ReturnRequestCreateDto dto)
        {
            if (dto.Quantity <= 0)
            {
                return BadRequest(new { message = "Quantity must be greater than zero." });
            }

            if (string.IsNullOrWhiteSpace(dto.CustomerReason))
            {
                return BadRequest(new { message = "Customer return reason is required." });
            }

            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
            {
                return Unauthorized(new { message = "Invalid customer token context." });
            }

            var order = await _context.Orders
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.Id == dto.OrderId);

            if (order == null)
            {
                return NotFound(new { message = "The specified order was not found." });
            }

            // Security guard: Ensure this order belongs to the requesting customer
            if (order.UserId != userId)
            {
                return Forbid();
            }

            // Status verification: Only allow returns on paid/shipped orders
            if (order.Status == "Pending" || order.Status == "Cancelled")
            {
                return BadRequest(new { message = "Cannot request a return for a pending or cancelled order." });
            }

            // Item purchase verification
            var orderItem = order.OrderItems.FirstOrDefault(oi => oi.VariantId == dto.VariantId);
            if (orderItem == null)
            {
                return BadRequest(new { message = "This variant was not purchased in the specified order." });
            }

            // Quantity limits verification including previous tickets
            var totalAlreadyRequested = await _context.ReturnRequests
                .Where(rr => rr.OrderId == dto.OrderId && rr.ProductVariantId == dto.VariantId && rr.Status != ReturnRequestStatus.Rejected)
                .SumAsync(rr => rr.Quantity);

            if (totalAlreadyRequested + dto.Quantity > orderItem.Quantity)
            {
                return BadRequest(new { 
                    message = $"The total return request quantity exceeds the purchased limit. " +
                              $"Purchased: {orderItem.Quantity}, Already requested/approved: {totalAlreadyRequested}, Requested now: {dto.Quantity}" 
                });
            }

            var request = new ReturnRequest
            {
                OrderId = dto.OrderId,
                ProductVariantId = dto.VariantId,
                Quantity = dto.Quantity,
                CustomerReason = dto.CustomerReason,
                CustomerNotes = dto.CustomerNotes,
                Status = ReturnRequestStatus.PendingReview,
                CreatedAt = DateTime.UtcNow
            };

            _context.ReturnRequests.Add(request);
            await _context.SaveChangesAsync();

            var responseDto = new ReturnRequestResponseDto
            {
                Id = request.Id,
                OrderId = request.OrderId,
                ProductVariantId = request.ProductVariantId,
                Quantity = request.Quantity,
                CustomerReason = request.CustomerReason,
                CustomerNotes = request.CustomerNotes,
                Status = request.Status.ToString(),
                CreatedAt = request.CreatedAt
            };

            return CreatedAtAction(nameof(GetRequestById), new { id = request.Id }, responseDto);
        }

        // PUT: api/ReturnRequests/{id}/review
        [HttpPut("{id}/review")]
        //[Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> ReviewRequest(Guid id, [FromBody] ReturnRequestReviewDto dto)
        {
            if (!Enum.TryParse<ReturnRequestStatus>(dto.Status, true, out var reviewStatus) || reviewStatus == ReturnRequestStatus.PendingReview)
            {
                return BadRequest(new { message = "Status must be either Approved or Rejected." });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var ticket = await _context.ReturnRequests
                    .Include(rr => rr.ProductVariant)
                        .ThenInclude(v => v!.Product)
                    .FirstOrDefaultAsync(rr => rr.Id == id);

                if (ticket == null)
                {
                    return NotFound(new { message = "Return request ticket not found." });
                }

                if (ticket.Status != ReturnRequestStatus.PendingReview)
                {
                    return BadRequest(new { message = "This ticket has already been processed." });
                }

                ticket.Status = reviewStatus;
                ticket.AdminNotes = dto.AdminNotes;

                // Inventory Update logic upon approval
                if (reviewStatus == ReturnRequestStatus.Approved && dto.IsRestockable)
                {
                    if (ticket.ProductVariant == null)
                    {
                        return BadRequest(new { message = "Associated product variant not found; cannot restock." });
                    }

                    // Increment variant physical quantity
                    ticket.ProductVariant.PhysicalQuantity += ticket.Quantity;

                    // Log StockMovement audit ledger entry
                    var movement = new StockMovement
                    {
                        ProductVariantId = ticket.ProductVariantId,
                        QuantityChanged = ticket.Quantity,
                        MovementType = StockMovementType.CustomerReturn,
                        ChangedBy = GetUserEmail(),
                        ReasonCode = ticket.CustomerReason,
                        ReferenceId = ticket.OrderId.ToString(),
                        Notes = string.IsNullOrWhiteSpace(dto.AdminNotes) ? "Restocked via Return Review Approval" : dto.AdminNotes,
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.StockMovements.Add(movement);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Cache invalidation
                if (reviewStatus == ReturnRequestStatus.Approved && dto.IsRestockable && ticket.ProductVariant?.Product != null)
                {
                    await InvalidateProductCacheAsync(ticket.ProductVariant.Product.CategoryId);
                }

                return Ok(new { 
                    message = $"Ticket has been successfully resolved to: {reviewStatus}.",
                    status = reviewStatus.ToString(),
                    adminNotes = ticket.AdminNotes
                });
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogWarning(ex, "Concurrency conflict during review of return request {TicketId}", id);
                return Conflict(new { message = "The inventory record was updated by another process. Please reload and retry." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to resolve return request {TicketId}", id);
                return StatusCode(500, new { message = "An error occurred while processing the review update.", error = ex.Message });
            }
        }

        // GET: api/ReturnRequests/{id}
        [HttpGet("{id}")]
        //[Authorize]
        public async Task<IActionResult> GetRequestById(Guid id)
        {
            var ticketQuery = _context.ReturnRequests.Where(rr => rr.Id == id);
            var ticketList = await MapToResponseDtosAsync(ticketQuery);
            var responseDto = ticketList.FirstOrDefault();

            if (responseDto == null)
            {
                return NotFound(new { message = "Return request ticket not found." });
            }

            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
            {
                return Unauthorized();
            }

            var order = await _context.Orders.Select(o => new { o.Id, o.UserId }).FirstOrDefaultAsync(o => o.Id == responseDto.OrderId);
            if (order == null)
            {
                return BadRequest("Order mapping is invalid.");
            }

            if (order.UserId != userId && !User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                return Forbid();
            }

            return Ok(responseDto);
        }

        // GET: api/ReturnRequests (All tickets for Admins/Managers)
        [HttpGet]
        //[Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> GetAllRequests([FromQuery] string? status)
        {
            try
            {
                var query = _context.ReturnRequests.AsQueryable();

                if (!string.IsNullOrEmpty(status) && Enum.TryParse<ReturnRequestStatus>(status, true, out var filterStatus))
                {
                    query = query.Where(rr => rr.Status == filterStatus);
                }

                var list = await MapToResponseDtosAsync(query);
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve return tickets.");
                return StatusCode(500, new { message = "An error occurred while fetching return request tickets." });
            }
        }

        // GET: api/ReturnRequests/my-requests (Active Customer's history)
        [HttpGet("my-requests")]
        //[Authorize]
        public async Task<IActionResult> GetMyRequests()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
            {
                return Unauthorized();
            }

            try
            {
                var query = _context.ReturnRequests.Where(rr => rr.Order!.UserId == userId);
                var list = await MapToResponseDtosAsync(query);
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve personal return tickets for user {UserId}", userId);
                return StatusCode(500, new { message = "An error occurred while fetching your personal return requests." });
            }
        }

        private async Task<List<ReturnRequestResponseDto>> MapToResponseDtosAsync(IQueryable<ReturnRequest> query)
        {
            var tickets = await query
                .Include(rr => rr.Order)
                    .ThenInclude(o => o!.User)
                .Include(rr => rr.ProductVariant)
                    .ThenInclude(v => v!.Product)
                .OrderByDescending(rr => rr.CreatedAt)
                .ToListAsync();

            var productIds = tickets
                .Where(rr => rr.ProductVariant?.Product != null)
                .Select(rr => rr.ProductVariant!.ProductId)
                .Distinct()
                .ToList();

            var images = await _context.ProductImages
                .Where(img => productIds.Contains(img.ProductId))
                .ToListAsync();

            return tickets.Select(rr =>
            {
                var variant = rr.ProductVariant;
                var product = variant?.Product;
                var user = rr.Order?.User;

                var productImages = images.Where(img => img.ProductId == variant?.ProductId).ToList();
                var itemImage = productImages.FirstOrDefault(img => string.Equals(img.Color, variant?.Color, StringComparison.OrdinalIgnoreCase))?.ImageUrl
                    ?? productImages.FirstOrDefault(img => img.IsPrimary)?.ImageUrl
                    ?? productImages.FirstOrDefault()?.ImageUrl
                    ?? string.Empty;

                return new ReturnRequestResponseDto
                {
                    Id = rr.Id,
                    OrderId = rr.OrderId,
                    ProductVariantId = rr.ProductVariantId,
                    ProductId = product?.Id,
                    ProductName = product?.Name ?? "Unknown Product",
                    Size = variant?.Size ?? string.Empty,
                    Color = variant?.Color ?? string.Empty,
                    Sku = variant?.Sku ?? string.Empty,
                    ImageUrl = itemImage,
                    CustomerName = user?.FullName ?? "Guest Customer",
                    CustomerEmail = user?.Email ?? "N/A",
                    Quantity = rr.Quantity,
                    CustomerReason = rr.CustomerReason,
                    CustomerNotes = rr.CustomerNotes,
                    AdminNotes = rr.AdminNotes,
                    Status = rr.Status.ToString(),
                    CreatedAt = rr.CreatedAt
                };
            }).ToList();
        }

        private string GetUserEmail()
        {
            return User.FindFirst(ClaimTypes.Email)?.Value 
                ?? User.Identity?.Name 
                ?? "System";
        }

        private async Task InvalidateProductCacheAsync(Guid categoryId)
        {
            try
            {
                var defaultKeys = new List<string>
                {
                    "products:cat=:disc=:q=:p=1:sz=10",
                    "products:cat=:disc=False:q=:p=1:sz=10",
                    "products:cat=:disc=True:q=:p=1:sz=10",
                    $"products:cat={categoryId}:disc=:q=:p=1:sz=10",
                    $"products:cat={categoryId}:disc=False:q=:p=1:sz=10",
                    $"products:cat={categoryId}:disc=True:q=:p=1:sz=10"
                };

                foreach (var key in defaultKeys)
                {
                    await _cache.RemoveAsync(key);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache invalidation failed for category {CategoryId}.", categoryId);
            }
        }
    }
}
