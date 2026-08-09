using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Hubs;
using Ecomerce_dotnet.Models;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin,Manager")]
    public class InventoryController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IDistributedCache _cache;
        private readonly ILogger<InventoryController> _logger;
        private readonly IHubContext<NotificationHub> _notificationHubContext;

        public InventoryController(
            ApplicationDbContext context, 
            IDistributedCache cache, 
            ILogger<InventoryController> logger,
            IHubContext<NotificationHub> notificationHubContext)
        {
            _context = context;
            _cache = cache;
            _logger = logger;
            _notificationHubContext = notificationHubContext;
        }

        // POST: api/Inventory/restock
        [HttpPost("restock")]
        public async Task<IActionResult> Restock([FromBody] StockAdjustmentRequestDto dto)
        {
            if (dto.Quantity <= 0)
            {
                return BadRequest(new { message = "Restock quantity must be greater than zero." });
            }

            if (string.IsNullOrWhiteSpace(dto.ReasonCode))
            {
                return BadRequest(new { message = "Reason code is required." });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var variant = await _context.ProductVariants
                    .Include(v => v.Product)
                    .FirstOrDefaultAsync(v => v.Id == dto.VariantId);

                if (variant == null)
                {
                    return NotFound(new { message = "Product variant not found." });
                }

                variant.PhysicalQuantity += dto.Quantity;
                if (dto.UnitCost > 0)
                {
                    variant.CostPrice = dto.UnitCost;
                }

                var movement = new StockMovement
                {
                    ProductVariantId = variant.Id,
                    QuantityChanged = dto.Quantity,
                    UnitCost = dto.UnitCost,
                    MovementType = StockMovementType.Restock,
                    ChangedBy = GetUserEmail(),
                    ReasonCode = dto.ReasonCode,
                    ReferenceId = dto.ReferenceId,
                    Notes = dto.Notes,
                    CreatedAt = DateTime.UtcNow
                };

                _context.StockMovements.Add(movement);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                if (variant.Product != null)
                {
                    await InvalidateProductCacheAsync(variant.Product.CategoryId);
                }

                return Ok(new { message = "Stock restocked successfully.", newPhysicalQuantity = variant.PhysicalQuantity });
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogWarning(ex, "Concurrency conflict occurred during restock of variant {VariantId}", dto.VariantId);
                return Conflict(new { message = "The product variant was modified by another user. Please reload the data and try again." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to restock variant {VariantId}", dto.VariantId);
                return StatusCode(500, new { message = "An error occurred while processing the restock operation.", error = ex.Message });
            }
        }

        // POST: api/Inventory/discard
        [HttpPost("discard")]
        public async Task<IActionResult> Discard([FromBody] StockAdjustmentRequestDto dto)
        {
            if (dto.Quantity <= 0)
            {
                return BadRequest(new { message = "Discard quantity must be greater than zero." });
            }

            if (string.IsNullOrWhiteSpace(dto.ReasonCode))
            {
                return BadRequest(new { message = "Reason code is required." });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var variant = await _context.ProductVariants
                    .Include(v => v.Product)
                    .FirstOrDefaultAsync(v => v.Id == dto.VariantId);

                if (variant == null)
                {
                    return NotFound(new { message = "Product variant not found." });
                }

                int availablePhysical = variant.PhysicalQuantity - variant.ReservedQuantity;
                if (variant.PhysicalQuantity < dto.Quantity)
                {
                    return BadRequest(new { message = $"Cannot discard quantity ({dto.Quantity}) exceeding physical stock ({variant.PhysicalQuantity})." });
                }
                
                if (availablePhysical < dto.Quantity)
                {
                    return BadRequest(new { message = $"Cannot discard quantity ({dto.Quantity}) because some physical items are reserved ({variant.ReservedQuantity} reserved of {variant.PhysicalQuantity} total). Max discardable: {availablePhysical}." });
                }

                variant.PhysicalQuantity -= dto.Quantity;

                var movement = new StockMovement
                {
                    ProductVariantId = variant.Id,
                    QuantityChanged = -dto.Quantity,
                    MovementType = StockMovementType.DamagedDiscard,
                    ChangedBy = GetUserEmail(),
                    ReasonCode = dto.ReasonCode,
                    ReferenceId = dto.ReferenceId,
                    Notes = dto.Notes,
                    CreatedAt = DateTime.UtcNow
                };

                _context.StockMovements.Add(movement);

                Notification? lowStockNotif = CheckAndCreateLowStockNotification(variant);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                if (lowStockNotif != null)
                {
                    await BroadcastNotificationAsync(lowStockNotif);
                }

                if (variant.Product != null)
                {
                    await InvalidateProductCacheAsync(variant.Product.CategoryId);
                }

                return Ok(new { message = "Damaged stock discarded successfully.", newPhysicalQuantity = variant.PhysicalQuantity });
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogWarning(ex, "Concurrency conflict occurred during discard of variant {VariantId}", dto.VariantId);
                return Conflict(new { message = "The product variant was modified by another user. Please reload the data and try again." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to discard variant {VariantId}", dto.VariantId);
                return StatusCode(500, new { message = "An error occurred while processing the discard operation.", error = ex.Message });
            }
        }

        // POST: api/Inventory/return
        [HttpPost("return")]
        public async Task<IActionResult> Return([FromBody] StockAdjustmentRequestDto dto)
        {
            if (dto.Quantity <= 0)
            {
                return BadRequest(new { message = "Return quantity must be greater than zero." });
            }

            if (string.IsNullOrWhiteSpace(dto.ReasonCode))
            {
                return BadRequest(new { message = "Reason code is required." });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var variant = await _context.ProductVariants
                    .Include(v => v.Product)
                    .FirstOrDefaultAsync(v => v.Id == dto.VariantId);

                if (variant == null)
                {
                    return NotFound(new { message = "Product variant not found." });
                }

                variant.PhysicalQuantity += dto.Quantity;

                var movement = new StockMovement
                {
                    ProductVariantId = variant.Id,
                    QuantityChanged = dto.Quantity,
                    MovementType = StockMovementType.CustomerReturn,
                    ChangedBy = GetUserEmail(),
                    ReasonCode = dto.ReasonCode,
                    ReferenceId = dto.ReferenceId,
                    Notes = dto.Notes,
                    CreatedAt = DateTime.UtcNow
                };

                _context.StockMovements.Add(movement);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                if (variant.Product != null)
                {
                    await InvalidateProductCacheAsync(variant.Product.CategoryId);
                }

                return Ok(new { message = "Customer return processed successfully.", newPhysicalQuantity = variant.PhysicalQuantity });
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogWarning(ex, "Concurrency conflict occurred during customer return of variant {VariantId}", dto.VariantId);
                return Conflict(new { message = "The product variant was modified by another user. Please reload the data and try again." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to process customer return for variant {VariantId}", dto.VariantId);
                return StatusCode(500, new { message = "An error occurred while processing the customer return operation.", error = ex.Message });
            }
        }

        // POST: api/Inventory/correct
        [HttpPost("correct")]
        public async Task<IActionResult> Correct([FromBody] StockAdjustmentRequestDto dto)
        {
            if (dto.Quantity < 0)
            {
                return BadRequest(new { message = "Verified physical quantity cannot be negative." });
            }

            if (string.IsNullOrWhiteSpace(dto.ReasonCode))
            {
                return BadRequest(new { message = "Reason code is required." });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var variant = await _context.ProductVariants
                    .Include(v => v.Product)
                    .FirstOrDefaultAsync(v => v.Id == dto.VariantId);

                if (variant == null)
                {
                    return NotFound(new { message = "Product variant not found." });
                }

                int oldPhysical = variant.PhysicalQuantity;
                int difference = dto.Quantity - oldPhysical;

                if (difference == 0)
                {
                    return Ok(new { message = "Verified physical stock matches current levels. No correction needed.", newPhysicalQuantity = variant.PhysicalQuantity });
                }

                if (dto.Quantity < variant.ReservedQuantity)
                {
                    return BadRequest(new { message = $"Cannot set physical quantity to {dto.Quantity} because it is below the current active reserved reservations ({variant.ReservedQuantity} reserved)." });
                }

                variant.PhysicalQuantity = dto.Quantity;

                var movement = new StockMovement
                {
                    ProductVariantId = variant.Id,
                    QuantityChanged = difference,
                    MovementType = StockMovementType.AuditCorrection,
                    ChangedBy = GetUserEmail(),
                    ReasonCode = dto.ReasonCode,
                    ReferenceId = dto.ReferenceId,
                    Notes = dto.Notes,
                    CreatedAt = DateTime.UtcNow
                };

                _context.StockMovements.Add(movement);

                Notification? lowStockNotif = CheckAndCreateLowStockNotification(variant);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                if (lowStockNotif != null)
                {
                    await BroadcastNotificationAsync(lowStockNotif);
                }

                if (variant.Product != null)
                {
                    await InvalidateProductCacheAsync(variant.Product.CategoryId);
                }

                return Ok(new { message = "Audit correction logged successfully.", newPhysicalQuantity = variant.PhysicalQuantity });
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogWarning(ex, "Concurrency conflict occurred during audit correction of variant {VariantId}", dto.VariantId);
                return Conflict(new { message = "The product variant was modified by another user. Please reload the data and try again." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to perform audit correction on variant {VariantId}", dto.VariantId);
                return StatusCode(500, new { message = "An error occurred while processing the audit correction.", error = ex.Message });
            }
        }

        // GET: api/Inventory/variants/{variantId}/history
        [HttpGet("variants/{variantId}/history")]
        public async Task<IActionResult> GetVariantHistory(Guid variantId)
        {
            try
            {
                var exists = await _context.ProductVariants.AnyAsync(v => v.Id == variantId);
                if (!exists)
                {
                    return NotFound(new { message = "Product variant not found." });
                }

                var history = await _context.StockMovements
                    .Where(sm => sm.ProductVariantId == variantId)
                    .OrderByDescending(sm => sm.CreatedAt)
                    .Select(sm => new StockMovementHistoryDto
                    {
                        Id = sm.Id,
                        ProductVariantId = sm.ProductVariantId,
                        QuantityChanged = sm.QuantityChanged,
                        UnitCost = sm.UnitCost,
                        MovementType = sm.MovementType.ToString(),
                        ChangedBy = sm.ChangedBy,
                        ReasonCode = sm.ReasonCode,
                        ReferenceId = sm.ReferenceId,
                        Notes = sm.Notes,
                        CreatedAt = sm.CreatedAt
                    })
                    .ToListAsync();

                return Ok(history);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve stock movement history for variant {VariantId}", variantId);
                return StatusCode(500, new { message = "An error occurred while retrieving variant stock history.", error = ex.Message });
            }
        }

        private Notification? CheckAndCreateLowStockNotification(ProductVariant variant)
        {
            int availableStock = variant.PhysicalQuantity - variant.ReservedQuantity;
            if (availableStock <= 5)
            {
                var notif = new Notification
                {
                    Title = "Low Stock Alert",
                    Message = $"Low stock alert for SKU '{variant.Sku}'. Remaining available stock: {availableStock}.",
                    Type = "LowStock",
                    TargetUrl = "/inventory",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Notifications.Add(notif);
                return notif;
            }
            return null;
        }

        private async Task BroadcastNotificationAsync(Notification notification)
        {
            try
            {
                await _notificationHubContext.Clients.Group("Admins").SendAsync("ReceiveNotification", new
                {
                    id = notification.Id,
                    title = notification.Title,
                    message = notification.Message,
                    type = notification.Type,
                    targetUrl = notification.TargetUrl,
                    isRead = notification.IsRead,
                    createdAt = notification.CreatedAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast low stock notification for notification {Id}", notification.Id);
            }
        }

        private string GetUserEmail()
        {
            return User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value 
                ?? User.Identity?.Name 
                ?? "System";
        }

        private async Task InvalidateProductCacheAsync(Guid categoryId)
        {
            try
            {
                var defaultKeys = new List<string>();

                string[] cats = new string[] { "", categoryId.ToString() };
                bool?[] discs = new bool?[] { null, false, true };
                int[] pageSizes = new int[] { 10, 20, 50 };
                int[] pageNumbers = new int[] { 1, 2, 3, 4, 5 };

                foreach (var cat in cats)
                {
                    foreach (var disc in discs)
                    {
                        foreach (var sz in pageSizes)
                        {
                            foreach (var p in pageNumbers)
                            {
                                defaultKeys.Add($"products:cat={cat}:brand=:disc={disc}:q=:minp=:maxp=:gen=:p={p}:sz={sz}");
                                defaultKeys.Add($"products:cat={cat}:disc={disc}:q=:p={p}:sz={sz}");
                            }
                        }
                    }
                }

                defaultKeys.Add("products:top-selling:count=5");
                defaultKeys.Add("products:top-selling:count=10");
                defaultKeys.Add("products:top-selling:count=20");

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
