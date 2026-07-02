using Ecomerce_dotnet.Data;
using Microsoft.EntityFrameworkCore;

namespace Ecomerce_dotnet.Services
{
    public class OrderExpiryWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OrderExpiryWorker> _logger;

        // 15 minutes checkout window before inventory is released
        private readonly TimeSpan _expiryTimeout = TimeSpan.FromMinutes(15);
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

        public OrderExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<OrderExpiryWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Stock Reservation Background Worker is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ReleaseExpiredReservationsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while executing the expired stock cleanup.");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }
        }

        private async Task ReleaseExpiredReservationsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var absoluteCutoffTime = DateTime.UtcNow - _expiryTimeout;

            // Adjusted to look for string "Pending" and use your OrderDate property
            var expiredOrders = await context.Orders
                .Include(o => o.OrderItems)
                .Where(o => o.Status == "Pending" && o.OrderDate <= absoluteCutoffTime)
                .ToListAsync();

            if (!expiredOrders.Any()) return;

            _logger.LogInformation($"Found {expiredOrders.Count} expired orders to clean up.");

            using var transaction = await context.Database.BeginTransactionAsync();
            try
            {
                foreach (var order in expiredOrders)
                {
                    foreach (var item in order.OrderItems)
                    {
                        // Adjusted to use your item.VariantId property
                        var variant = await context.ProductVariants
                            .FirstOrDefaultAsync(v => v.Id == item.VariantId);

                        if (variant != null)
                        {
                            // Shift the stock out of reservation back to available
                            variant.ReservedQuantity = Math.Max(0, variant.ReservedQuantity - item.Quantity);
                        }
                    }

                    // Marked as "Cancelled" based on your model status options
                    order.Status = "Cancelled";
                }

                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Successfully restored stock for expired orders.");
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
