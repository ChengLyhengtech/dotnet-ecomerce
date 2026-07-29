using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomerce_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class AddTargetGenderToProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TargetGender",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetGender",
                table: "Products");
        }
    }
}
