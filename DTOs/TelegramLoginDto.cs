namespace Ecomerce_dotnet.DTOs
{
    public class TelegramLoginDto
    {
        public long Id { get; set; }
        public required string First_Name { get; set; }
        public string? Last_Name { get; set; }
        public string? Username { get; set; }
        public string? Photo_Url { get; set; }
        public long Auth_Date { get; set; } // Unix timestamp
        public required string Hash { get; set; }
    }
}
