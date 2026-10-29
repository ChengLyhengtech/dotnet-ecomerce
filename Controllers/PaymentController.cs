using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Hubs;
using Ecomerce_dotnet.Models;
using Ecomerce_dotnet.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("ApiPolicy")]
    public class PaymentController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly BakongQrService _qrService;
        private readonly IHubContext<OrderHub> _hubContext;
        private readonly IHubContext<NotificationHub> _notificationHubContext;

        public PaymentController(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            BakongQrService qrService,
            IHubContext<OrderHub> hubContext,
            IHubContext<NotificationHub> notificationHubContext)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _config = config;
            _qrService = qrService;
            _hubContext = hubContext;
            _notificationHubContext = notificationHubContext;
        }

        // =========================================================================
        // 1. POST /api/generate-khqr
        // =========================================================================
        [HttpPost("/api/generate-khqr")]
        [HttpPost("generate-khqr")]
        public async Task<IActionResult> GenerateKhqr([FromBody] GenerateKhqrRequestDto? request)
        {
            try
            {
                request ??= new GenerateKhqrRequestDto();
                decimal amount = request.Amount > 0 ? request.Amount : 0.01m;
                string currency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency.ToUpper();

                string invoice = $"INV-{DateTime.UtcNow:yyyyMMddHHmmssfff}";

                string bakongId = _config["BakongSettings:BakongAccountID"] ?? _config["BakongSettings:BakongId"] ?? "vann_sak@bkrt";
                string merchantId = _config["BakongSettings:MerchantID"] ?? "123456";
                string merchantName = _config["BakongSettings:MerchantName"] ?? "ChengLyheng";
                string merchantCity = _config["BakongSettings:MerchantCity"] ?? "Phnom Penh";
                string acquiringBank = _config["BakongSettings:AcquiringBank"] ?? "Bakong";
                string mobileNumber = _config["BakongSettings:MobileNumber"] ?? "85586513225";
                string storeLabel = _config["BakongSettings:StoreLabel"] ?? "E-Commerce Store";
                string terminalLabel = _config["BakongSettings:TerminalLabel"] ?? "Cashier_1";
                string mcc = _config["BakongSettings:MerchantCategoryCode"] ?? "5999";

                var (qrString, md5) = _qrService.GenerateMerchantQr(
                    bakongId,
                    merchantName,
                    merchantCity,
                    acquiringBank,
                    amount,
                    currency,
                    invoice,
                    merchantId,
                    mobileNumber,
                    storeLabel,
                    terminalLabel,
                    mcc,
                    expirationMinutes: 15
                );

                var payment = new Payment
                {
                    Invoice = invoice,
                    Amount = amount,
                    Currency = currency,
                    Md5 = md5,
                    QrString = qrString,
                    Status = "PENDING",
                    CreatedAt = DateTime.UtcNow,
                    OrderId = request.OrderId
                };

                _context.Payments.Add(payment);

                if (request.OrderId.HasValue && request.OrderId.Value != Guid.Empty)
                {
                    var order = await _context.Orders.FindAsync(request.OrderId.Value);
                    if (order != null)
                    {
                        order.QrCode = qrString;
                        order.QrMd5 = md5;
                        order.QrExpiration = DateTime.UtcNow.AddMinutes(15);
                        order.PaymentMethod = "khqr";
                    }
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    qrString = payment.QrString,
                    qr_code = payment.QrString,
                    md5 = payment.Md5,
                    qr_md5 = payment.Md5,
                    invoice = payment.Invoice,
                    amount = payment.Amount,
                    currency = payment.Currency,
                    merchant_name = merchantName,
                    qr_expiration = DateTime.UtcNow.AddMinutes(15).ToString("o")
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Failed to generate KHQR string: {ex.Message}" });
            }
        }

        // =========================================================================
        // 2. GET /api/qr-image/{invoice}
        // =========================================================================
        [HttpGet("/api/qr-image/{invoice}")]
        [HttpGet("qr-image/{invoice}")]
        public async Task<IActionResult> GetQrImage(string invoice)
        {
            if (string.IsNullOrWhiteSpace(invoice))
            {
                return BadRequest("Invoice parameter is required.");
            }

            bool isGuid = Guid.TryParse(invoice, out Guid parsedGuid);

            var payment = await _context.Payments.FirstOrDefaultAsync(p => 
                p.Invoice == invoice || 
                p.Md5 == invoice || 
                (isGuid && (p.OrderId == parsedGuid || p.Id == parsedGuid))
            );

            if (payment == null || string.IsNullOrWhiteSpace(payment.QrString))
            {
                return NotFound("Invoice or QR code string not found.");
            }

            byte[] imageBytes = _qrService.GenerateQrCodePng(payment.QrString);
            return File(imageBytes, "image/png");
        }

        // =========================================================================
        // 3A. GET /api/check-payment/{invoice}
        // =========================================================================
        [HttpGet("/api/check-payment/{invoice}")]
        [HttpGet("check-payment/{invoice}")]
        public Task<IActionResult> CheckPaymentGet(string invoice)
        {
            return PerformCheckPaymentAsync(invoice, null);
        }

        // =========================================================================
        // 3B. POST /api/check-payment/{invoice}
        // =========================================================================
        [HttpPost("/api/check-payment/{invoice}")]
        [HttpPost("check-payment/{invoice}")]
        public Task<IActionResult> CheckPaymentPost(string invoice, [FromBody] CheckPaymentRequestDto? request = null)
        {
            return PerformCheckPaymentAsync(invoice, request);
        }

        // =========================================================================
        // 4. POST /api/v1/generate_deeplink_by_qr
        // =========================================================================
        [HttpPost("/api/v1/generate_deeplink_by_qr")]
        [HttpPost("generate-deeplink")]
        public async Task<IActionResult> GenerateDeeplink([FromBody] GenerateDeeplinkRequestDto? request)
        {
            string? qrString = request?.QrString ?? request?.Qr;
            if (string.IsNullOrWhiteSpace(qrString))
            {
                return BadRequest(new { success = false, message = "qrString parameter is required." });
            }

            string baseUrl = GetConfigValue("BakongBaseUrl", "BaseApiUrl", "https://api-bakong.nbc.gov.kh/v1");
            string token = GetConfigValue("Token", "BakongAccessToken", "");

            string jsonResult = await _qrService.GenerateDeeplinkAsync(baseUrl, token, qrString, request?.SourceInfo);
            return Content(jsonResult, "application/json");
        }

        // =========================================================================
        // 5. DEVELOPER TEST ENDPOINT: POST /api/payment/simulate-success/{orderId}
        // =========================================================================
        [HttpPost("simulate-success/{orderId:guid}")]
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

            var payment = order.Payment;
            string mockTxHash = $"DEV-TEST-TX-{Guid.NewGuid().ToString()[..8].ToUpper()}";
            return await ProcessPaymentSuccessAsync(payment, order, mockTxHash, "dev_acc_from", "dev_acc_to", "Simulated via Developer Test API Endpoint.");
        }

        // =========================================================================
        // 6. PATCH/POST: /api/payment/fail or /api/payment/fail/{identifier}
        // Allows frontend to fail/cancel payment and immediately release reserved stock
        // =========================================================================
        [HttpPatch("fail/{identifier?}")]
        [HttpPost("fail/{identifier?}")]
        [HttpPatch("cancel/{identifier?}")]
        [HttpPost("cancel/{identifier?}")]
        [HttpPatch("{identifier}/fail")]
        public async Task<IActionResult> FailPayment([FromRoute] string? identifier = null, [FromBody] FailPaymentRequestDto? request = null)
        {
            try
            {
                string? rawKey = identifier 
                    ?? request?.Invoice 
                    ?? request?.QrMd5 
                    ?? request?.QrMd5Alt 
                    ?? request?.Md5 
                    ?? request?.OrderId?.ToString() 
                    ?? request?.PaymentId?.ToString();

                if (string.IsNullOrWhiteSpace(rawKey))
                {
                    return BadRequest(new { success = false, message = "Payment identifier (invoice, md5, orderId, or paymentId) is required." });
                }

                rawKey = rawKey.Trim();
                string lookupKey = rawKey;
                if (lookupKey.StartsWith("INV-", StringComparison.OrdinalIgnoreCase) && lookupKey.Length > 21)
                {
                    lookupKey = lookupKey.Substring(0, 21);
                }

                bool isGuid = Guid.TryParse(lookupKey, out Guid parsedGuid);

                // Find Payment
                var payment = await _context.Payments
                    .Include(p => p.Order!)
                        .ThenInclude(o => o.OrderItems)
                            .ThenInclude(oi => oi.ProductVariant)
                    .FirstOrDefaultAsync(p => 
                        p.Invoice == lookupKey || 
                        p.Invoice == rawKey ||
                        p.Md5 == lookupKey || 
                        p.Md5 == rawKey ||
                        p.BakongTransactionRef == lookupKey ||
                        (isGuid && (p.OrderId == parsedGuid || p.Id == parsedGuid))
                    );

                Order? order = payment?.Order;

                // Fallback to Orders table if Payment link missing
                if (order == null && (isGuid || !string.IsNullOrEmpty(lookupKey)))
                {
                    order = await _context.Orders
                        .Include(o => o.Payment)
                        .Include(o => o.OrderItems)
                            .ThenInclude(oi => oi.ProductVariant)
                        .FirstOrDefaultAsync(o => 
                            o.QrMd5 == lookupKey || 
                            o.QrMd5 == rawKey ||
                            (isGuid && o.Id == parsedGuid)
                        );

                    payment ??= order?.Payment;
                }

                if (payment == null && order == null)
                {
                    return NotFound(new { success = false, message = "Payment or Order record not found for the specified identifier." });
                }

                // Guard: Do not allow failing already settled payments
                if ((payment != null && payment.Status == "PAID") || (order != null && order.Status == "Paid"))
                {
                    return BadRequest(new { success = false, message = "Cannot fail a payment that has already been successfully paid and settled." });
                }

                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    if (payment != null)
                    {
                        payment.Status = "FAILED";
                        if (!string.IsNullOrWhiteSpace(request?.Reason))
                        {
                            payment.Description = $"Cancelled/Failed: {request.Reason}";
                        }
                    }

                    if (order != null)
                    {
                        // If order is pending, release reserved stock back to available pool
                        if (string.Equals(order.Status, "Pending", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(order.Status, OrderStatus.Pending.ToString(), StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (var item in order.OrderItems)
                            {
                                var variant = item.ProductVariant ?? await _context.ProductVariants.FindAsync(item.VariantId);
                                if (variant != null)
                                {
                                    variant.ReservedQuantity = Math.Max(0, variant.ReservedQuantity - item.Quantity);
                                }
                            }

                            order.Status = OrderStatus.Cancelled.ToString();
                        }
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    // Broadcast Real-time SignalR Event
                    var targetOrderId = order?.Id ?? payment?.OrderId;
                    if (targetOrderId.HasValue)
                    {
                        var failPayload = new
                        {
                            orderId = targetOrderId.Value,
                            invoice = payment?.Invoice ?? order?.QrCode,
                            status = "FAILED",
                            orderStatus = order?.Status ?? "Cancelled",
                            paid = false,
                            reason = request?.Reason ?? "Payment marked as failed/cancelled by client."
                        };

                        await _hubContext.Clients.Group($"order_{targetOrderId.Value}").SendAsync("ReceivePaymentFailed", failPayload);
                    }

                    return Ok(new
                    {
                        success = true,
                        message = "Payment status updated to FAILED and reserved stock was released.",
                        status = "FAILED",
                        invoice = payment?.Invoice,
                        orderId = order?.Id ?? payment?.OrderId,
                        orderStatus = order?.Status
                    });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return StatusCode(500, new { success = false, message = $"Failed to update payment status: {ex.Message}" });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"An unexpected error occurred: {ex.Message}" });
            }
        }

        // Core Status Checking Engine (Supports Invoice, MD5, OrderId, and UserId)
        private async Task<IActionResult> PerformCheckPaymentAsync(string identifier, CheckPaymentRequestDto? request)
        {
            string? rawKey = request?.QrMd5 
                ?? request?.QrMd5Alt 
                ?? request?.Md5 
                ?? request?.Invoice 
                ?? request?.OrderId 
                ?? identifier;

            if (string.IsNullOrWhiteSpace(rawKey) || rawKey.Trim() == "null" || rawKey.Trim() == "undefined")
            {
                return Ok(new { status = "PENDING", paid = false, success = false });
            }

            rawKey = rawKey.Trim();

            // Auto-clean concatenated Invoice suffixes if full Tag 62 string passed (e.g., "INV-202607310654193780211855...")
            string lookupKey = rawKey;
            if (lookupKey.StartsWith("INV-", StringComparison.OrdinalIgnoreCase) && lookupKey.Length > 21)
            {
                lookupKey = lookupKey.Substring(0, 21);
            }

            bool isGuid = Guid.TryParse(lookupKey, out Guid parsedGuid);

            Console.WriteLine($"\n🔍 ==================== [CHECK PAYMENT STATUS] ====================");
            Console.WriteLine($"👉 Raw Request Input: '{rawKey}'");
            Console.WriteLine($"👉 Cleaned Lookup Key: '{lookupKey}'");

            // 1. Multi-field Database Search across Payments table
            var payment = await _context.Payments
                .Include(p => p.Order!)
                    .ThenInclude(o => o.OrderItems)
                        .ThenInclude(oi => oi.ProductVariant)
                            .ThenInclude(v => v!.Product)
                .FirstOrDefaultAsync(p => 
                    p.Invoice == lookupKey || 
                    p.Invoice == rawKey ||
                    p.Md5 == lookupKey || 
                    p.Md5 == rawKey ||
                    p.BakongTransactionRef == lookupKey ||
                    (isGuid && (p.OrderId == parsedGuid || p.Id == parsedGuid))
                );

            Order? order = payment?.Order;

            // 2. Fallback Search across Orders table if Payment link missing
            if (order == null && (isGuid || !string.IsNullOrEmpty(lookupKey)))
            {
                order = await _context.Orders
                    .Include(o => o.Payment)
                    .Include(o => o.OrderItems)
                        .ThenInclude(oi => oi.ProductVariant)
                            .ThenInclude(v => v!.Product)
                    .FirstOrDefaultAsync(o => 
                        o.QrMd5 == lookupKey || 
                        o.QrMd5 == rawKey ||
                        (isGuid && (o.Id == parsedGuid || o.UserId == parsedGuid))
                    );

                payment ??= order?.Payment;
            }

            Console.WriteLine($"👉 DB Payment Record: {(payment != null ? $"FOUND (Status: {payment.Status}, Invoice: {payment.Invoice}, MD5: {payment.Md5})" : "NOT FOUND IN PAYMENTS TABLE")}");
            Console.WriteLine($"👉 DB Order Record:   {(order != null ? $"FOUND (Status: {order.Status}, Id: {order.Id})" : "NOT FOUND IN ORDERS TABLE")}");

            // 3. Early Exit if already marked PAID
            if ((payment != null && payment.Status == "PAID") || (order != null && order.Status == "Paid"))
            {
                Console.WriteLine($"✅ Payment is ALREADY SETTLED ('PAID') in Database.");
                Console.WriteLine($"====================================================================\n");
                return Ok(new
                {
                    status = "PAID",
                    paid = true,
                    success = true,
                    message = "Payment verified successfully.",
                    invoice = payment?.Invoice,
                    order_id = order?.Id ?? payment?.OrderId,
                    amount = payment?.Amount ?? order?.TotalAmount,
                    currency = payment?.Currency ?? "USD",
                    paid_at = payment?.PaidAt?.ToString("o") ?? DateTime.UtcNow.ToString("o")
                });
            }

            // Early Exit if already marked FAILED or Cancelled
            if ((payment != null && payment.Status == "FAILED") || (order != null && order.Status == "Cancelled"))
            {
                Console.WriteLine($"❌ Payment or Order is FAILED / CANCELLED in Database.");
                Console.WriteLine($"====================================================================\n");
                return Ok(new
                {
                    status = "FAILED",
                    paid = false,
                    success = false,
                    message = "Payment has failed or was cancelled.",
                    invoice = payment?.Invoice,
                    order_id = order?.Id ?? payment?.OrderId
                });
            }

            string md5ToVerify = payment?.Md5 ?? order?.QrMd5 ?? lookupKey;
            string baseUrl = GetConfigValue("BakongBaseUrl", "BaseApiUrl", "https://api-bakong.nbc.gov.kh/v1").TrimEnd('/');
            string token = GetConfigValue("Token", "BakongAccessToken", "");

            Console.WriteLine($"📡 Querying NBC Bakong Open API...");
            Console.WriteLine($"📡 Endpoint URL: {baseUrl}/check_transaction_by_md5");
            Console.WriteLine($"📡 MD5 Target:  {md5ToVerify}");
            Console.WriteLine($"📡 Token Auth:  {(string.IsNullOrWhiteSpace(token) ? "❌ NO TOKEN CONFIGURED!" : "✅ Bearer " + token.Substring(0, Math.Min(15, token.Length)) + "...")}");

            // 4. Query NBC Bakong Open API check_transaction_by_md5
            var client = _httpClientFactory.CreateClient();
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var requestPayload = new { md5 = md5ToVerify };
            var jsonContent = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync($"{baseUrl}/check_transaction_by_md5", jsonContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ HTTP Network Error querying Bakong API: {ex.Message}");
                Console.WriteLine($"====================================================================\n");
                return Ok(new { status = "PENDING", paid = false, success = false });
            }

            var responseString = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"📩 Bakong Response HTTP Status Code: {(int)response.StatusCode} ({response.StatusCode})");
            Console.WriteLine($"📩 Bakong Response Payload: {responseString}");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"⚠️ Bakong API returned non-success HTTP status.");
                Console.WriteLine($"====================================================================\n");
                return Ok(new { status = "PENDING", paid = false, success = false });
            }

            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;

            int responseCode = root.TryGetProperty("responseCode", out var codeProp) && codeProp.ValueKind == JsonValueKind.Number ? codeProp.GetInt32() : -1;

            bool hasDataObj = root.TryGetProperty("data", out var dataObj) && dataObj.ValueKind == JsonValueKind.Object;
            string? dataStatus = hasDataObj && dataObj.TryGetProperty("status", out var sProp) && sProp.ValueKind == JsonValueKind.String ? sProp.GetString()?.ToUpper() : null;

            Console.WriteLine($"🔍 Parsed responseCode: {responseCode}, status: '{dataStatus}'");

            // 5. On NBC Success (responseCode == 0 or status == SUCCESS/COMPLETED) -> Execute Unit of Work
            if (responseCode == 0 || dataStatus == "SUCCESS" || dataStatus == "COMPLETED")
            {
                Console.WriteLine($"🎉 BAKONG PAYMENT CONFIRMED! Proceeding to settle order & stock...");
                string hash = hasDataObj && dataObj.TryGetProperty("hash", out var hProp) && hProp.ValueKind == JsonValueKind.String ? hProp.GetString() ?? md5ToVerify : md5ToVerify;
                string? fromAcc = hasDataObj && dataObj.TryGetProperty("fromAccountId", out var fProp) && fProp.ValueKind == JsonValueKind.String ? fProp.GetString() : null;
                string? toAcc = hasDataObj && dataObj.TryGetProperty("toAccountId", out var tProp) && tProp.ValueKind == JsonValueKind.String ? tProp.GetString() : null;
                string? desc = hasDataObj && dataObj.TryGetProperty("description", out var dProp) && dProp.ValueKind == JsonValueKind.String ? dProp.GetString() : "Bakong KHQR Payment Verified";

                return await ProcessPaymentSuccessAsync(payment, order, hash, fromAcc, toAcc, desc ?? "Bakong KHQR Payment Verified");
            }

            Console.WriteLine($"⏳ Payment is still PENDING on Bakong network.");
            Console.WriteLine($"====================================================================\n");
            return Ok(new { status = "PENDING", paid = false, success = false });
        }

        // Shared Settlement Engine
        private async Task<IActionResult> ProcessPaymentSuccessAsync(
            Payment? payment,
            Order? order,
            string transactionRef,
            string? fromAccountId,
            string? toAccountId,
            string description)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                DateTime paidAt = DateTime.UtcNow;

                if (payment == null)
                {
                    payment = new Payment
                    {
                        Invoice = $"INV-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
                        Amount = order?.TotalAmount ?? 0.01m,
                        Currency = "USD",
                        Md5 = order?.QrMd5 ?? transactionRef,
                        QrString = order?.QrCode ?? "",
                        CreatedAt = DateTime.UtcNow,
                        OrderId = order?.Id
                    };
                    _context.Payments.Add(payment);
                }

                payment.Status = "PAID";
                payment.PaidAt = paidAt;
                payment.BakongTransactionRef = transactionRef;
                payment.FromAccountId = fromAccountId;
                payment.ToAccountId = toAccountId;
                payment.Description = description;

                if (order != null)
                {
                    order.Status = "Paid";

                    // Create System Notification
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

                    var lowStockNotifs = new List<Notification>();

                    // Settle Inventory Stock
                    foreach (var item in order.OrderItems)
                    {
                        var variant = item.ProductVariant;
                        if (variant != null)
                        {
                            variant.PhysicalQuantity = Math.Max(0, variant.PhysicalQuantity - item.Quantity);
                            variant.ReservedQuantity = Math.Max(0, variant.ReservedQuantity - item.Quantity);

                            _context.StockMovements.Add(new StockMovement
                            {
                                ProductVariantId = variant.Id,
                                QuantityChanged = -item.Quantity,
                                MovementType = StockMovementType.Sale,
                                ChangedBy = "System (Bakong KHQR)",
                                CreatedAt = DateTime.UtcNow
                            });

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
                                lowStockNotifs.Add(lowStockNotif);
                            }
                        }
                    }

                    // Clear Cart Items
                    var customerCartItems = await _context.CartItems
                        .Where(ci => ci.Cart!.UserId == order.UserId)
                        .ToListAsync();

                    if (customerCartItems.Any())
                    {
                        _context.CartItems.RemoveRange(customerCartItems);
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    // SignalR Real-Time Broadcasting
                    var orderEventPayload = new
                    {
                        orderId = order.Id,
                        invoice = payment.Invoice,
                        status = "PAID",
                        paid = true,
                        totalAmount = order.TotalAmount,
                        paidAt = paidAt.ToString("o"),
                        transactionRef = payment.BakongTransactionRef
                    };

                    await _hubContext.Clients.Group($"order_{order.Id}").SendAsync("ReceivePaymentSuccess", orderEventPayload);
                    await _hubContext.Clients.All.SendAsync("ReceiveNewOrder", orderEventPayload);

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
                }
                else
                {
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }

                return Ok(new { status = "PAID", paid = true, success = true, invoice = payment.Invoice });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { status = "FAILED", paid = false, success = false, message = $"Payment confirmation failed: {ex.Message}" });
            }
        }

        private string GetConfigValue(string primaryKey, string fallbackKey, string defaultValue)
        {
            return _config[primaryKey]
                ?? _config[$"BakongSettings:{primaryKey}"]
                ?? _config[$"BakongSettings:{fallbackKey}"]
                ?? _config[fallbackKey]
                ?? defaultValue;
        }
    }
}