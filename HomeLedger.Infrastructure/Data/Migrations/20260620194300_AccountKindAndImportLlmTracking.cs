using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeLedger.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccountKindAndImportLlmTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SuggestionSource",
                table: "ImportItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LlmAvailabilityNotes",
                table: "ImportBatches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LlmCategorizationAvailable",
                table: "ImportBatches",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LlmCategorizedCount",
                table: "ImportBatches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "LlmClassificationAvailable",
                table: "ImportBatches",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LlmClassifiedCount",
                table: "ImportBatches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "LlmConfiguredAtImport",
                table: "ImportBatches",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PdfExtractedWithLlm",
                table: "ImportBatches",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SuggestionSource",
                table: "ImportItems");

            migrationBuilder.DropColumn(
                name: "LlmAvailabilityNotes",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "LlmCategorizationAvailable",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "LlmCategorizedCount",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "LlmClassificationAvailable",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "LlmClassifiedCount",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "LlmConfiguredAtImport",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "PdfExtractedWithLlm",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Accounts");
        }
    }
}
