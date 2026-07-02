namespace Ecomerce_dotnet.DTOs
{
    public class GoogleLoginDto
    {
        // This is the raw JWT identity token returned by the Google frontend SDK
        public required string IdToken { get; set; }
    }
}
