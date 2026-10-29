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
        public async Task<IActionResult> GetSummary([FromQuery] string? range, [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
        {
            try
            {
                var now = DateTime.UtcNow;
                DateTime start;
                DateTime end;

                if (!string.IsNullOrWhiteSpace(range) && !range.Equals("custom", StringComparison.OrdinalIgnoreCase))
                {
                    switch (range.Trim().ToLowerInvariant())
                    {
                        case "today":
                            start = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);
                            end = DateTime.SpecifyKind(now.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
                            break;
                        case "this_week":
                        case "week":
                            int diff = (7 + (int)now.DayOfWeek - (int)DayOfWeek.Monday) % 7;
                            start = DateTime.SpecifyKind(now.Date.AddDays(-diff), DateTimeKind.Utc);
                            end = DateTime.SpecifyKind(start.AddDays(7).AddTicks(-1), DateTimeKind.Utc);
                            break;
                        case "this_month":
                        case "month":
                            start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                            end = start.AddMonths(1).AddTicks(-1);
                            break;
                        case "this_year":
                        case "year":
                            start = new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                            end = start.AddYears(1).AddTicks(-1);
                            break;
                        case "last_30_days":
                            end = now;
                            start = end.AddDays(-30);
                            break;
                        default:
                            start = startDate.HasValue 
                                ? DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Utc) 
                                : now.AddDays(-30);
                            end = endDate.HasValue 
                                ? (endDate.Value.TimeOfDay == TimeSpan.Zero 
                                    ? DateTime.SpecifyKind(endDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc) 
                                    : DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc))
                                : now;
                            break;
                    }
                }
                else
                {
                    start = startDate.HasValue 
                        ? DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Utc) 
                        : now.AddDays(-30);
                    end = endDate.HasValue 
                        ? (endDate.Value.TimeOfDay == TimeSpan.Zero 
                            ? DateTime.SpecifyKind(endDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc) 
                            : DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc))
                        : now;
                }

                if (start > end)
                {
                    return BadRequest(new { message = "Start date cannot be after end date." });
                }

                _logger.LogInformation("Generating dashboard summary from {Start} to {End}", start, end);

                // 1. Calculate Financials: Gross Revenue, Total COGS, Net Profit, Profit Margin %, Total Orders, and AOV
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

                // 2. Compute Sales Trends: Day-by-day comparison (Monday - Sunday) for current week vs previous week
                DateTime trendRefDate = end > now ? now : end;
                int daysSinceMonday = (7 + (int)trendRefDate.Date.DayOfWeek - (int)DayOfWeek.Monday) % 7;
                DateTime currentWeekMonday = DateTime.SpecifyKind(trendRefDate.Date.AddDays(-daysSinceMonday), DateTimeKind.Utc);
                DateTime previousWeekMonday = currentWeekMonday.AddDays(-7);

                var trendOrders = await _context.Orders
                    .Where(o => o.OrderDate >= previousWeekMonday && o.OrderDate < currentWeekMonday.AddDays(7) && o.Status != "Pending" && o.Status != "Cancelled")
                    .Select(o => new { o.OrderDate, o.TotalAmount })
                    .ToListAsync();

                string[] dayLabels = new[] { "M", "T", "W", "T", "F", "S", "S" };
                var salesTrends = new List<SalesTrendPointDto>();

                for (int i = 0; i < 7; i++)
                {
                    var currDayStart = currentWeekMonday.AddDays(i);
                    var currDayEnd = currDayStart.AddDays(1);
                    var prevDayStart = previousWeekMonday.AddDays(i);
                    var prevDayEnd = prevDayStart.AddDays(1);

                    decimal currDaySales = trendOrders
                        .Where(o => o.OrderDate >= currDayStart && o.OrderDate < currDayEnd)
                        .Sum(o => o.TotalAmount);

                    decimal prevDaySales = trendOrders
                        .Where(o => o.OrderDate >= prevDayStart && o.OrderDate < prevDayEnd)
                        .Sum(o => o.TotalAmount);

                    salesTrends.Add(new SalesTrendPointDto
                    {
                        Label = dayLabels[i],
                        CurrentPeriod = currDaySales,
                        PreviousPeriod = prevDaySales
                    });
                }

                // 3. Compute Transaction Statistics: 10-point curve time series across [start, end]
                int intervals = 10;
                var totalSeconds = (end - start).TotalSeconds;
                var intervalSeconds = totalSeconds > 0 ? totalSeconds / intervals : 1;

                var rangeOrders = await _context.Orders
                    .Where(o => o.OrderDate >= start && o.OrderDate <= end && o.Status != "Pending" && o.Status != "Cancelled")
                    .Select(o => new { o.OrderDate, o.TotalAmount })
                    .ToListAsync();

                var salesSeries = new List<decimal>();
                var transactionsSeries = new List<int>();

                for (int i = 0; i < intervals; i++)
                {
                    var bucketStart = start.AddSeconds(i * intervalSeconds);
                    var bucketEnd = (i == intervals - 1) ? end : start.AddSeconds((i + 1) * intervalSeconds);

                    var bucketOrders = rangeOrders
                        .Where(o => o.OrderDate >= bucketStart && (i == intervals - 1 ? o.OrderDate <= bucketEnd : o.OrderDate < bucketEnd))
                        .ToList();

                    salesSeries.Add(bucketOrders.Sum(o => o.TotalAmount));
                    transactionsSeries.Add(bucketOrders.Count);
                }

                decimal peakValue = salesSeries.Count > 0 ? salesSeries.Max() : 0m;

                var transactionStatistics = new TransactionStatisticsDto
                {
                    PeakValue = peakValue,
                    SalesSeries = salesSeries,
                    TransactionsSeries = transactionsSeries
                };

                // 4. Query Total Registered Customer User Account Count
                var totalUserCount = await _context.Users.CountAsync();

                // 5. Query Inventory Alerts: Product variants with computed available stock (Physical - Reserved) <= 5
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

                // 6. Query Geospatial Logistics Matrix
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

                // Base query for top performers within target date range
                var validOrderItemsQuery = _context.OrderItems
                    .Where(oi => oi.Order != null 
                        && oi.Order.OrderDate >= start 
                        && oi.Order.OrderDate <= end 
                        && oi.Order.Status != "Pending" 
                        && oi.Order.Status != "Cancelled");

                // 7. Top 5 Products by Quantity Sold
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

                // 8. Top 5 Brands by Revenue
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

                // 9. Top 5 Categories by Revenue
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
                    SalesTrends = salesTrends,
                    TransactionStatistics = transactionStatistics,
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

        [HttpGet("trends")]
        public async Task<IActionResult> GetTrends([FromQuery] string? range, [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
        {
            var summaryResult = await GetSummary(range, startDate, endDate);
            if (summaryResult is OkObjectResult okResult && okResult.Value is DashboardSummaryDto summary)
            {
                return Ok(new
                {
                    salesTrends = summary.SalesTrends,
                    transactionStatistics = summary.TransactionStatistics
                });
            }

            return summaryResult;
        }
    }
}
