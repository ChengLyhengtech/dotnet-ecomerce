using System;
using System.Text.Json.Serialization;

namespace Ecomerce_dotnet.DTOs
{
    public class GenerateKhqrRequestDto
    {
        public Guid? OrderId { get; set; }
        public decimal Amount { get; set; } = 0.01m;
        public string Currency { get; set; } = "USD";
    }

    public class CheckPaymentRequestDto
    {
        [JsonPropertyName("qr_md5")]
        public string? QrMd5 { get; set; }

        [JsonPropertyName("qrMd5")]
        public string? QrMd5Alt { get; set; }

        [JsonPropertyName("md5")]
        public string? Md5 { get; set; }

        [JsonPropertyName("invoice")]
        public string? Invoice { get; set; }

        [JsonPropertyName("orderId")]
        public string? OrderId { get; set; }
    }

    public class GenerateDeeplinkRequestDto
    {
        [JsonPropertyName("qrString")]
        public string? QrString { get; set; }

        [JsonPropertyName("qr")]
        public string? Qr { get; set; }

        [JsonPropertyName("sourceInfo")]
        public object? SourceInfo { get; set; }
    }

    public class FailPaymentRequestDto
    {
        [JsonPropertyName("invoice")]
        public string? Invoice { get; set; }

        [JsonPropertyName("qr_md5")]
        public string? QrMd5 { get; set; }

        [JsonPropertyName("qrMd5")]
        public string? QrMd5Alt { get; set; }

        [JsonPropertyName("md5")]
        public string? Md5 { get; set; }

        [JsonPropertyName("orderId")]
        public Guid? OrderId { get; set; }

        [JsonPropertyName("paymentId")]
        public Guid? PaymentId { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }
}
