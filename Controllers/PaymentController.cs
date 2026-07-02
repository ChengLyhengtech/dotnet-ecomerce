using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.Models;
using Ecomerce_dotnet.Services;
using Ecomerce_dotnet.Hubs;
using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly IHubContext<OrderHub> _hubContext; // SignalR Context dependency injection
        private readonly CartItem _cartItem;

        public PaymentController(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            IHubContext<OrderHub> hubContext,
            CartItem cartItem)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _config = config;
            _hubContext = hubContext;
            _cartItem = cartItem;
        }

        [HttpPost("check-status/{orderId}")]
        public async Task<IActionResult> CheckPayment(Guid orderId, [FromBody] JsonElement body)
        {
            if (!body.TryGetProperty("qr_md5", out var qrMd5Prop))
            {
                return BadRequest("Missing required parameter: qr_md5");
            }
            string inputMd5 = qrMd5Prop.GetString() ?? "";

            // Eagerly load items and profiles so the real-time broadcast payload is rich with info
            var order = await _context.Orders
                .Include(o => o.Payment)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.ProductVariant)
                        .ThenInclude(v => v!.Product)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return NotFound("Order record not found.");

            if (order.Status == "Paid" && order.Payment?.Status == "Success")
            {
                return Ok(new { success = true, message = "Payment already confirmed" });
            }

            if (order.QrExpiration.HasValue && DateTime.UtcNow > order.QrExpiration.Value)
            {
                return BadRequest(new { success = false, message = "QR code has expired." });
            }

            if (order.QrMd5 != inputMd5)
            {
                return BadRequest(new { success = false, message = "Invalid QR code token mapping mismatch." });
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
                return StatusCode((int)response.StatusCode, "Upstream lookup communication failure with NBC processing gateway.");
            }

            var responseString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;

            int responseCode = root.GetProperty("responseCode").GetInt32();

            if (responseCode == 0 && root.TryGetProperty("data", out var dataObj) && dataObj.TryGetProperty("hash", out var hashProp))
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // 1. Mutate tracking status states
                    order.Status = "Paid";

                    var payment = new Payment
                    {
                        OrderId = order.Id,
                        BakongTransactionRef = hashProp.GetString() ?? "",
                        Status = "Success",
                        PaidAt = DateTime.UtcNow,
                        Amount = order.TotalAmount,
                        Description = "Verified via Bakong lookup utility engine."
                    };
                    _context.Payments.Add(payment);

                    // 2. Permanently settle warehouse inventories
                    foreach (var item in order.OrderItems)
                    {
                        var variant = item.ProductVariant;
                        if (variant != null)
                        {
                            // Permanently reduce available warehouse quantities out of physical stock layers
                            variant.PhysicalQuantity = Math.Max(0, variant.PhysicalQuantity - item.Quantity);
                            variant.ReservedQuantity = Math.Max(0, variant.ReservedQuantity - item.Quantity);
                        }
                    }

                    // Use the Cart navigation property to reach the UserId table column
                    var customerCartItems = await _context.CartItems
                        .Where(ci => ci.Cart!.UserId == order.UserId)
                        .ToListAsync();

                    if (customerCartItems.Any())
                    {
                        _context.CartItems.RemoveRange(customerCartItems);
                    }
                    // ====================================================================

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    // 3. BROADCAST REAL-TIME NOTIFICATION TO MANAGER DASHBOARD MAP
                    // This pushes a clean, scannable map tracking payload directly to your Angular view via open websockets
                    var broadCastPayload = new
                    {
                        orderId = order.Id,
                        orderDate = order.OrderDate.ToString("o"),
                        totalAmount = order.TotalAmount,
                        contactPhone = order.ContactPhone,
                        shippingAddress = order.ShippingAddress,
                        latitude = order.DeliveryLatitude,
                        longitude = order.DeliveryLongitude,
                        items = order.OrderItems.Select(oi => new {
                            productName = oi.ProductVariant?.Product?.Name ?? "Unknown",
                            size = oi.ProductVariant?.Size,
                            color = oi.ProductVariant?.Color,
                            qty = oi.Quantity
                        })
                    };

                    await _hubContext.Clients.All.SendAsync("ReceiveNewOrder", broadCastPayload);

                    return Ok(new { success = true, message = "Payment confirmed successfully." });
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }

            return NotFound(new { success = false, message = "Payment transaction record not yet detected on NBC ledger network." });
        }
    }
}