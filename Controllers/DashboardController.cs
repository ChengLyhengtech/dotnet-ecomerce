using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin,Manager")]
    public class DashboardController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(ApplicationDbContext context, ILogger<DashboardController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
        {
            try
            {
                // Default date range fallback to the trailing 30 days if not provided
                DateTime end = endDate ?? DateTime.UtcNow;
                DateTime start = startDate ?? end.AddDays(-30);

                if (start > end)
                {
                    return BadRequest(new { message = "Start date cannot be after end date." });
                }

                _logger.LogInformation("Generating dashboard summary from {Start} to {End}", start, end);

                // 1. Calculate Financials: Gross Revenue, Total Orders, and Average Order Value (AOV)
                // Filtered by date range and non-Pending, non-Cancelled orders
                var financialsQuery = await _context.Orders
                    .Where(o => o.OrderDate >= start && o.OrderDate <= end && o.Status != "Pending" && o.Status != "Cancelled")
                    .GroupBy(o => 1)
                    .Select(g => new
                    {
                        TotalOrders = g.Count(),
                        GrossRevenue = g.Sum(o => o.TotalAmount)
                    })
                    .FirstOrDefaultAsync();

                var financials = new FinancialsDto
                {
                    GrossRevenue = financialsQuery?.GrossRevenue ?? 0m,
                    TotalOrders = financialsQuery?.TotalOrders ?? 0,
                    AverageOrderValue = (financialsQuery != null && financialsQuery.TotalOrders > 0)
                        ? Math.Round(financialsQuery.GrossRevenue / financialsQuery.TotalOrders, 2)
                        : 0m
                };

                // 2. Query Inventory Alerts: Product variants with computed available stock (Physical - Reserved) <= 5
                // Across non-deleted products only
                var inventoryAlerts = await _context.ProductVariants
                    .Include(v => v.Product)
                    .Where(v => v.Product != null && !v.Product.IsDeleted && (v.PhysicalQuantity - v.ReservedQuantity) <= 5)
                    .Select(v => new InventoryAlertDto
                    {
                        VariantId = v.Id,
                        ProductId = v.ProductId,
                        ProductName = v.Product!.Name,
                        Sku = v.Sku,
                        Color = v.Color,
                        Size = v.Size,
                        PhysicalQuantity = v.PhysicalQuantity,
                        ReservedQuantity = v.ReservedQuantity,
                        AvailableStock = v.PhysicalQuantity - v.ReservedQuantity
                    })
                    .ToListAsync();

                // 3. Query Geospatial Logistics Matrix: Order latitude/longitude points inside date range
                // Filter out Cancelled orders to focus on pending/active/completed logistics
                var geospatialLogistics = await _context.Orders
                    .Where(o => o.OrderDate >= start && o.OrderDate <= end && o.Status != "Cancelled")
                    .Select(o => new GeospatialLogisticsDto
                    {
                        OrderId = o.Id,
                        DeliveryLatitude = o.DeliveryLatitude,
                        DeliveryLongitude = o.DeliveryLongitude,
                        TotalAmount = o.TotalAmount
                    })
                    .ToListAsync();

                var dashboardSummary = new DashboardSummaryDto
                {
                    Financials = financials,
                    InventoryAlerts = inventoryAlerts,
                    GeospatialLogistics = geospatialLogistics
                };

                return Ok(dashboardSummary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve dashboard analytics summary.");
                return StatusCode(500, new { message = "An error occurred while generating the dashboard analytics summary.", error = ex.Message });
            }
        }
    }
}
