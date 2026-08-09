using System;
using System.Collections.Generic;
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
    //[Authorize(Roles = "Admin,Manager")]
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

                // 1. Calculate Financials: Gross Revenue, Total COGS, Net Profit, Profit Margin %, Total Orders, and AOV
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

                var totalCogs = await _context.OrderItems
                    .Where(oi => oi.Order != null 
                        && oi.Order.OrderDate >= start 
                        && oi.Order.OrderDate <= end 
                        && oi.Order.Status != "Pending" 
                        && oi.Order.Status != "Cancelled")
                    .SumAsync(oi => (decimal?)(oi.Quantity * oi.UnitCost)) ?? 0m;

                decimal grossRevenue = financialsQuery?.GrossRevenue ?? 0m;
                int totalOrders = financialsQuery?.TotalOrders ?? 0;
                decimal netProfit = grossRevenue - totalCogs;
                decimal profitMarginPercentage = grossRevenue > 0 
                    ? Math.Round((netProfit / grossRevenue) * 100m, 2) 
                    : 0m;
                decimal averageOrderValue = totalOrders > 0 
                    ? Math.Round(grossRevenue / totalOrders, 2) 
                    : 0m;

                var financials = new FinancialsDto
                {
                    GrossRevenue = grossRevenue,
                    TotalCOGS = totalCogs,
                    NetProfit = netProfit,
                    ProfitMarginPercentage = profitMarginPercentage,
                    TotalOrders = totalOrders,
                    AverageOrderValue = averageOrderValue
                };

                // 2. Query Total Registered Customer User Account Count
                var totalUserCount = await _context.Users.CountAsync();

                // 3. Query Inventory Alerts: Product variants with computed available stock (Physical - Reserved) <= 5
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

                // 4. Query Geospatial Logistics Matrix: Order latitude/longitude points inside date range
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

                // Base query for top performers within target date range (non-Pending, non-Cancelled orders)
                var validOrderItemsQuery = _context.OrderItems
                    .Where(oi => oi.Order != null 
                        && oi.Order.OrderDate >= start 
                        && oi.Order.OrderDate <= end 
                        && oi.Order.Status != "Pending" 
                        && oi.Order.Status != "Cancelled");

                // 5. Top 5 Products by Quantity Sold
                var topProducts = await validOrderItemsQuery
                    .Where(oi => oi.ProductVariant != null && oi.ProductVariant.Product != null)
                    .GroupBy(oi => new { oi.ProductVariant!.Product!.Id, oi.ProductVariant!.Product!.Name })
                    .Select(g => new TopPerformerDto
                    {
                        Id = g.Key.Id,
                        Name = g.Key.Name,
                        TotalQuantitySold = g.Sum(x => x.Quantity),
                        TotalRevenue = g.Sum(x => x.Quantity * x.PriceAtPurchase)
                    })
                    .OrderByDescending(p => p.TotalQuantitySold)
                    .Take(5)
                    .ToListAsync();

                // 6. Top 5 Brands by Revenue
                var topBrands = await validOrderItemsQuery
                    .Where(oi => oi.ProductVariant != null && oi.ProductVariant.Product != null && oi.ProductVariant.Product.Brand != null)
                    .GroupBy(oi => new { oi.ProductVariant!.Product!.Brand!.Id, oi.ProductVariant!.Product!.Brand!.Name })
                    .Select(g => new TopPerformerDto
                    {
                        Id = g.Key.Id,
                        Name = g.Key.Name,
                        TotalQuantitySold = g.Sum(x => x.Quantity),
                        TotalRevenue = g.Sum(x => x.Quantity * x.PriceAtPurchase)
                    })
                    .OrderByDescending(b => b.TotalRevenue)
                    .Take(5)
                    .ToListAsync();

                // 7. Top 5 Categories by Revenue
                var topCategories = await validOrderItemsQuery
                    .Where(oi => oi.ProductVariant != null && oi.ProductVariant.Product != null && oi.ProductVariant.Product.Category != null)
                    .GroupBy(oi => new { oi.ProductVariant!.Product!.Category!.Id, oi.ProductVariant!.Product!.Category!.Name })
                    .Select(g => new TopPerformerDto
                    {
                        Id = g.Key.Id,
                        Name = g.Key.Name,
                        TotalQuantitySold = g.Sum(x => x.Quantity),
                        TotalRevenue = g.Sum(x => x.Quantity * x.PriceAtPurchase)
                    })
                    .OrderByDescending(c => c.TotalRevenue)
                    .Take(5)
                    .ToListAsync();

                var dashboardSummary = new DashboardSummaryDto
                {
                    Financials = financials,
                    TotalUserCount = totalUserCount,
                    InventoryAlerts = inventoryAlerts,
                    GeospatialLogistics = geospatialLogistics,
                    TopProducts = topProducts,
                    TopBrands = topBrands,
                    TopCategories = topCategories
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
