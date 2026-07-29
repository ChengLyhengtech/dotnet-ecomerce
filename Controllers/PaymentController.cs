using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.Hubs;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly IHubContext<OrderHub> _hubContext;
        private readonly IHubContext<NotificationHub> _notificationHubContext;

        public PaymentController(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            IHubContext<OrderHub> hubContext,
            IHubContext<NotificationHub> notificationHubContext)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _config = config;
            _hubContext = hubContext;
            _notificationHubContext = notificationHubContext;
        }

        // 1. POST: api/payment/check-status/{orderId} (Verify real NBC Bakong KHQR Transaction)
        [HttpPost("check-status/{orderId}")]
        public async Task<IActionResult> CheckPayment(Guid orderId, [FromBody] JsonElement body)
        {
            if (!body.TryGetProperty("qr_md5", out var qrMd5Prop))
            {
                return BadRequest(new { success = false, message = "Missing required parameter: qr_md5" });
            }
            string inputMd5 = qrMd5Prop.GetString() ?? "";

            var order = await _context.Orders
                .Include(o => o.Payment)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.ProductVariant)
                        .ThenInclude(v => v!.Product)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return NotFound(new { success = false, message = "Order record not found." });

            if (order.Status == "Paid" && order.Payment?.Status == "Success")
            {
                return Ok(new { success = true, message = "Payment already confirmed previously." });
            }

            if (order.QrExpiration.HasValue && DateTime.UtcNow > order.QrExpiration.Value)
            {
                return BadRequest(new { success = false, message = "QR code transaction period has expired." });
            }

            if (order.QrMd5 != inputMd5)
            {
                return BadRequest(new { success = false, message = "Invalid QR code MD5 token mapping mismatch." });
            }

            var baseUrl = _config["BakongSettings:BaseApiUrl"];
            var token = _config["BakongSettings:AccessToken"];

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var requestPayload = new { md5 = order.QrMd5 };
            var jsonContent = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{baseUrl}/check_transaction_by_md5", jsonContent);
            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, new { success = false, message = "Upstream NBC Bakong gateway lookup failed." });
            }

            var responseString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;

            int responseCode = root.GetProperty("responseCode").GetInt32();

            if (responseCode == 0 && root.TryGetProperty("data", out var dataObj) && dataObj.TryGetProperty("hash", out var hashProp))
            {
                string bakongTxHash = hashProp.GetString() ?? $"BAKONG-{Guid.NewGuid():N}";
                return await ProcessPaymentSuccessAsync(order, bakongTxHash, "Verified via NBC Bakong KHQR Ledger.");
            }

            return NotFound(new { success = false, message = "Payment transaction record not yet detected on NBC ledger network." });
        }

        // 2. DEVELOPER TEST ENDPOINT: api/payment/simulate-success/{orderId}
        // Allows frontend developers to simulate a successful payment instantly without making a real money transfer
        [HttpPost("simulate-success/{orderId}")]
        public async Task<IActionResult> SimulatePaymentSuccess(Guid orderId)
        {
            var order = await _context.Orders
                .Include(o => o.Payment)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.ProductVariant)
                        .ThenInclude(v => v!.Product)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
            {
                return NotFound(new { success = false, message = "Order record not found." });
            }

            if (order.Status == "Paid" && order.Payment?.Status == "Success")
            {
                return Ok(new { success = true, message = "Order is already marked as Paid." });
            }

            string mockTxHash = $"DEV-TEST-TX-{Guid.NewGuid().ToString()[..8].ToUpper()}";
            return await ProcessPaymentSuccessAsync(order, mockTxHash, "Simulated via Developer Test API Endpoint.");
        }

        // Shared Core Engine for Processing Payment Confirmation & SignalR Real-Time Broadcasting
        private async Task<IActionResult> ProcessPaymentSuccessAsync(Order order, string transactionRef, string description)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Mutate Order Status to Paid
                order.Status = "Paid";

                var payment = new Payment
                {
                    OrderId = order.Id,
                    BakongTransactionRef = transactionRef,
                    Status = "Success",
                    PaidAt = DateTime.UtcNow,
                    Amount = order.TotalAmount,
                    Description = description
                };
                _context.Payments.Add(payment);

                // 2. Create System Notification for Admins
                var notification = new Notification
                {
                    Title = $"New Order #{order.Id.ToString()[..8].ToUpper()}",
                    Message = $"Order #{order.Id.ToString()[..8].ToUpper()} placed with total ${order.TotalAmount:F2}.",
                    Type = "NewOrder",
                    TargetUrl = $"/orders/{order.Id}",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Notifications.Add(notification);

                var lowStockNotifications = new List<Notification>();

                // 3. Settle Inventory Stock
                foreach (var item in order.OrderItems)
                {
                    var variant = item.ProductVariant;
                    if (variant != null)
                    {
                        variant.PhysicalQuantity = Math.Max(0, variant.PhysicalQuantity - item.Quantity);
                        variant.ReservedQuantity = Math.Max(0, variant.ReservedQuantity - item.Quantity);

                        var movement = new StockMovement
                        {
                            ProductVariantId = variant.Id,
                            QuantityChanged = -item.Quantity,
                            MovementType = StockMovementType.Sale,
                            ChangedBy = "System (Payment)",
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.StockMovements.Add(movement);

                        int availableStock = variant.PhysicalQuantity - variant.ReservedQuantity;
                        if (availableStock <= 5)
                        {
                            var lowStockNotif = new Notification
                            {
                                Title = "Low Stock Alert",
                                Message = $"Low stock alert for SKU '{variant.Sku}'. Remaining available stock: {availableStock}.",
                                Type = "LowStock",
                                TargetUrl = "/inventory",
                                IsRead = false,
                                CreatedAt = DateTime.UtcNow
                            };
                            _context.Notifications.Add(lowStockNotif);
                            lowStockNotifications.Add(lowStockNotif);
                        }
                    }
                }

                // 4. Clear Customer's Cart Items
                var customerCartItems = await _context.CartItems
                    .Where(ci => ci.Cart!.UserId == order.UserId)
                    .ToListAsync();

                if (customerCartItems.Any())
                {
                    _context.CartItems.RemoveRange(customerCartItems);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // 5. SIGNALR REAL-TIME BROADCASTS

                // A. Broadcast to OrderHub listeners (Group order_{id} and All)
                var orderEventPayload = new
                {
                    orderId = order.Id,
                    status = "Paid",
                    totalAmount = order.TotalAmount,
                    paidAt = payment.PaidAt?.ToString("o"),
                    transactionRef = payment.BakongTransactionRef,
                    orderDate = order.OrderDate.ToString("o"),
                    contactPhone = order.ContactPhone,
                    shippingAddress = order.ShippingAddress,
                    latitude = order.DeliveryLatitude,
                    longitude = order.DeliveryLongitude,
                    items = order.OrderItems.Select(oi => new {
                        productName = oi.ProductVariant?.Product?.Name ?? "Unknown Product",
                        size = oi.ProductVariant?.Size,
                        color = oi.ProductVariant?.Color,
                        sku = oi.ProductVariant?.Sku,
                        qty = oi.Quantity,
                        price = oi.PriceAtPurchase
                    })
                };

                // Notify specific order subscriber group (Customer Payment Modal listener)
                await _hubContext.Clients.Group($"order_{order.Id}").SendAsync("ReceivePaymentSuccess", orderEventPayload);

                // Notify admin/manager dashboard
                await _hubContext.Clients.All.SendAsync("ReceiveNewOrder", orderEventPayload);

                // B. Broadcast to NotificationHub Admins group
                await _notificationHubContext.Clients.Group("Admins").SendAsync("ReceiveNotification", new
                {
                    id = notification.Id,
                    title = notification.Title,
                    message = notification.Message,
                    type = notification.Type,
                    targetUrl = notification.TargetUrl,
                    isRead = notification.IsRead,
                    createdAt = notification.CreatedAt.ToString("o")
                });

                foreach (var lowNotif in lowStockNotifications)
                {
                    await _notificationHubContext.Clients.Group("Admins").SendAsync("ReceiveNotification", new
                    {
                        id = lowNotif.Id,
                        title = lowNotif.Title,
                        message = lowNotif.Message,
                        type = lowNotif.Type,
                        targetUrl = lowNotif.TargetUrl,
                        isRead = lowNotif.IsRead,
                        createdAt = lowNotif.CreatedAt.ToString("o")
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = "Payment confirmed and processed successfully.",
                    orderId = order.Id,
                    transactionRef = payment.BakongTransactionRef
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { success = false, message = $"Payment confirmation failed: {ex.Message}" });
            }
        }
    }
}