using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptMerchantAndInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Merchant",
                table: "Transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceFileName",
                table: "Transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Merchant",
                table: "ImportItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceFileName",
                table: "ImportItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImportKind",
                table: "ImportBatches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Merchant",
                table: "ImportBatches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourcePath",
                table: "ImportBatches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Merchant",
                table: "Transactions",
                column: "Merchant");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_Merchant",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Merchant",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SourceFileName",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Merchant",
                table: "ImportItems");

            migrationBuilder.DropColumn(
                name: "SourceFileName",
                table: "ImportItems");

            migrationBuilder.DropColumn(
                name: "ImportKind",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "Merchant",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "SourcePath",
                table: "ImportBatches");
        }
    }
}
