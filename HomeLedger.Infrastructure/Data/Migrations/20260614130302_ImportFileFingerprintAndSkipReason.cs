using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeLedger.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImportFileFingerprintAndSkipReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SkipReason",
                table: "ImportItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileSha256",
                table: "ImportBatches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSizeBytes",
                table: "ImportBatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_FileSha256_FileSizeBytes_AccountId",
                table: "ImportBatches",
                columns: new[] { "FileSha256", "FileSizeBytes", "AccountId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ImportBatches_FileSha256_FileSizeBytes_AccountId",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "SkipReason",
                table: "ImportItems");

            migrationBuilder.DropColumn(
                name: "FileSha256",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "FileSizeBytes",
                table: "ImportBatches");
        }
    }
}
