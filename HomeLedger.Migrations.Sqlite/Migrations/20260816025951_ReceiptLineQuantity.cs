using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptLineQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "Transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuantityUnit",
                table: "Transactions",
                type: "TEXT",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "Transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "ImportItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuantityUnit",
                table: "ImportItems",
                type: "TEXT",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "ImportItems",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "QuantityUnit",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "ImportItems");

            migrationBuilder.DropColumn(
                name: "QuantityUnit",
                table: "ImportItems");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "ImportItems");
        }
    }
}
