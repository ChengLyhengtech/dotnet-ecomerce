using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomerce_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class RemoveShopProfileBannerUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BannerUrl",
                table: "ShopProfiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BannerUrl",
                table: "ShopProfiles",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
