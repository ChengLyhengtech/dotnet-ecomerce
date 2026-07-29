using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomerce_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class AddMobileAndDesktopBannerImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ImageUrl",
                table: "Banners",
                newName: "MobileImageUrl");

            migrationBuilder.AddColumn<string>(
                name: "DesktopImageUrl",
                table: "Banners",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DesktopImageUrl",
                table: "Banners");

            migrationBuilder.RenameColumn(
                name: "MobileImageUrl",
                table: "Banners",
                newName: "ImageUrl");
        }
    }
}
