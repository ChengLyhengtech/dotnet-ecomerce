namespace Ecomerce_dotnet.Models
{
    public class Payment
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid OrderId { get; set; }
        public Order? Order { get; set; }

        public string BakongTransactionRef { get; set; } = string.Empty; // Holds Bakong Hash
        public string Status { get; set; } = "Pending"; // Pending, Success, Failed
        public DateTime? PaidAt { get; set; }

        // --- Added Audit Details from Bakong Response ---
        public string? FromAccountId { get; set; }
        public string? ToAccountId { get; set; }
        public string? Currency { get; set; }
        public decimal Amount { get; set; }
        public string? Description { get; set; }
    }
}
