namespace Ecomerce_dotnet.Models
{
    public class AuditLog
    {
        public Guid Id { get; set; } = Guid.NewGuid(); // This is the Primary Key
        public string Action { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
