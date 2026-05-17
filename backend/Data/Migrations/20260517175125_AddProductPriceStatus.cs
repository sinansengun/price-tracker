using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductPriceStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PriceStatus",
                table: "Products",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriceStatus",
                table: "Products");
        }
    }
}
