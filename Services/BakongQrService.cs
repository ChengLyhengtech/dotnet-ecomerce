using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BakongSdk = kh.gov.nbc.bakong_khqr.BakongKHQR;
using BakongModel = kh.gov.nbc.bakong_khqr.model;
using QRCoder;

namespace Ecomerce_dotnet.Services
{
    public class BakongQrService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public BakongQrService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        // Computes standard MD5 Hash for Bakong lookup tracking
        public string ComputeMd5Hash(string rawQrString)
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(rawQrString);
            byte[] hashBytes = MD5.HashData(inputBytes);
            return Convert.ToHexString(hashBytes).ToLower();
        }

        // Generates Merchant KHQR string and MD5 according to NBC Bakong KHQR Specification v2.9
        public (string QrString, string Md5) GenerateMerchantQr(
            string bakongId,
            string merchantName,
            string merchantCity,
            string acquiringBank,
            decimal amount,
            string currency,
            string invoice,
            string? merchantId = null,
            string? mobileNumber = null,
            string? storeLabel = null,
            string? terminalLabel = null,
            string? merchantCategoryCode = null,
            int expirationMinutes = 15)
        {
            var khqrCurrency = currency.ToUpper() == "USD"
                ? BakongModel.KHQRCurrency.USD
                : BakongModel.KHQRCurrency.KHR;

            long expirationTimestamp = DateTimeOffset.UtcNow.AddMinutes(expirationMinutes).ToUnixTimeMilliseconds();

            var merchantInfo = new BakongModel.MerchantInfo
            {
                BakongAccountID = bakongId,
                MerchantID = string.IsNullOrWhiteSpace(merchantId) ? "123456" : merchantId,
                MerchantName = merchantName,
                MerchantCity = string.IsNullOrWhiteSpace(merchantCity) ? "Phnom Penh" : merchantCity,
                AcquiringBank = string.IsNullOrWhiteSpace(acquiringBank) ? "Bakong" : acquiringBank,
                Currency = khqrCurrency,
                Amount = (double)amount,
                BillNumber = invoice,
                MobileNumber = string.IsNullOrWhiteSpace(mobileNumber) ? "85586513225" : mobileNumber,
                StoreLabel = string.IsNullOrWhiteSpace(storeLabel) ? "E-Commerce Store" : storeLabel,
                TerminalLabel = string.IsNullOrWhiteSpace(terminalLabel) ? "Cashier_1" : terminalLabel,
                ExpirationTimestamp = expirationTimestamp,
                MerchantCategoryCode = string.IsNullOrWhiteSpace(merchantCategoryCode) ? "5999" : merchantCategoryCode
            };

            var response = BakongSdk.GenerateMerchant(merchantInfo);

            if (response != null && response.Status != null && response.Status.Code == 0 && response.Data != null)
            {
                string qr = response.Data.QR;
                string md5 = !string.IsNullOrEmpty(response.Data.MD5) ? response.Data.MD5 : ComputeMd5Hash(qr);
                return (qr, md5);
            }

            // Fallback EMVCo generation if Bakong SDK returns non-zero status
            string fallbackQr = GenerateFallbackMerchantQrString(bakongId, merchantName, merchantCity, amount, currency, invoice);
            string fallbackMd5 = ComputeMd5Hash(fallbackQr);
            return (fallbackQr, fallbackMd5);
        }

        // Generates Individual KHQR string and MD5 using official Bakong SDK
        public (string QrString, string Md5) GenerateIndividualQr(
            string bakongId,
            string merchantName,
            string merchantCity,
            decimal amount,
            string currency,
            int expirationMinutes = 15)
        {
            var khqrCurrency = currency.ToUpper() == "USD"
                ? BakongModel.KHQRCurrency.USD
                : BakongModel.KHQRCurrency.KHR;

            var individualInfo = new BakongModel.IndividualInfo
            {
                BakongAccountID = bakongId,
                MerchantName = merchantName,
                MerchantCity = string.IsNullOrWhiteSpace(merchantCity) ? "Phnom Penh" : merchantCity,
                Currency = khqrCurrency,
                Amount = (double)amount,
                ExpirationTimestamp = DateTimeOffset.UtcNow.AddMinutes(expirationMinutes).ToUnixTimeMilliseconds(),
                MerchantCategoryCode = "5999"
            };

            var response = BakongSdk.GenerateIndividual(individualInfo);

            if (response != null && response.Status != null && response.Status.Code == 0 && response.Data != null)
            {
                string qr = response.Data.QR;
                string md5 = !string.IsNullOrEmpty(response.Data.MD5) ? response.Data.MD5 : ComputeMd5Hash(qr);
                return (qr, md5);
            }

            string fallbackQr = GenerateFallbackMerchantQrString(bakongId, merchantName, merchantCity, amount, currency, "");
            string fallbackMd5 = ComputeMd5Hash(fallbackQr);
            return (fallbackQr, fallbackMd5);
        }

        // Backward compatibility overload returning string
        public string GenerateIndividualQrString(string username, string name, string city, decimal amount, string currency)
        {
            var (qr, _) = GenerateIndividualQr(username, name, city, amount, currency);
            return qr;
        }

        // Renders PNG QR Code image byte array from QR string using QRCoder
        public byte[] GenerateQrCodePng(string qrText)
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(qrText, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            return qrCode.GetGraphic(20);
        }

        // Generates Bakong DeepLink via Bakong Open API
        public async Task<string> GenerateDeeplinkAsync(string baseUrl, string token, string qrString, object? sourceInfo = null)
        {
            var client = _httpClientFactory.CreateClient();
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var payload = new
            {
                qr = qrString,
                sourceInfo = sourceInfo ?? new
                {
                    appIconUrl = "https://bakong.nbc.gov.kh/images/logo.png",
                    appName = "E-Commerce Store",
                    appDeepLinkCallback = "https://ecommerce.local/payment-callback"
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var url = $"{baseUrl.TrimEnd('/')}/generate_deeplink_by_qr";

            var response = await client.PostAsync(url, content);
            return await response.Content.ReadAsStringAsync();
        }

        private string GenerateFallbackMerchantQrString(string bakongId, string name, string city, decimal amount, string currency, string invoice)
        {
            var currencyCode = currency.ToUpper() == "USD" ? "840" : "116";

            var sb = new StringBuilder();
            sb.Append("000201");
            sb.Append("010212");

            var subTag00 = $"00{bakongId.Length:D2}{bakongId}";
            var tag29Data = $"{subTag00}";
            sb.Append($"29{tag29Data.Length:D2}{tag29Data}");

            sb.Append("52045999");
            sb.Append($"5303{currencyCode}");

            var amountStr = amount.ToString("F2");
            sb.Append($"54{amountStr.Length:D2}{amountStr}");

            sb.Append("5802KH");
            sb.Append($"59{name.Length:D2}{name}");
            sb.Append($"60{city.Length:D2}{city}");

            if (!string.IsNullOrWhiteSpace(invoice))
            {
                var billTag = $"01{invoice.Length:D2}{invoice}";
                sb.Append($"62{billTag.Length:D2}{billTag}");
            }

            string currentQr = sb.ToString() + "6304";
            ushort crc = ComputeCrc16(Encoding.UTF8.GetBytes(currentQr));
            return currentQr + crc.ToString("X4");
        }

        private ushort ComputeCrc16(byte[] bytes)
        {
            ushort crc = 0xFFFF;
            foreach (byte b in bytes)
            {
                crc ^= (ushort)(b << 8);
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 0x8000) != 0)
                        crc = (ushort)((crc << 1) ^ 0x1021);
                    else
                        crc <<= 1;
                }
            }
            return crc;
        }
    }
}