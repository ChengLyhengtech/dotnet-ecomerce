namespace Ecomerce_dotnet.DTOs
{
    public class OrderQueryDto
    {
        public string? Status { get; set; }
        public string? SearchTerm { get; set; } // Matches OrderId, Name, Email, or Phone
        public string? SortBy { get; set; }     // "date" or "amount"
        public bool IsDescending { get; set; } = true;

        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}
