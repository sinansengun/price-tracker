using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProductAlertSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AlertMode",
                table: "UserProducts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "automatic");

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountThresholdPercent",
                table: "UserProducts",
                type: "numeric(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlertMode",
                table: "UserProducts");

            migrationBuilder.DropColumn(
                name: "DiscountThresholdPercent",
                table: "UserProducts");
        }
    }
}
