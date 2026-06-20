using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeLedger.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImportProfilesAndSkipRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LinkedTransactionId",
                table: "Transactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestedSkipReason",
                table: "ImportItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImportProfileId",
                table: "Accounts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImportProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    LedgerEntityId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    UseLlmForUnmatched = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportProfiles_Entities_LedgerEntityId",
                        column: x => x.LedgerEntityId,
                        principalTable: "Entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImportSkipRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ImportProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Pattern = table.Column<string>(type: "TEXT", nullable: false),
                    MatchType = table.Column<int>(type: "INTEGER", nullable: false),
                    SkipKind = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportSkipRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportSkipRules_ImportProfiles_ImportProfileId",
                        column: x => x.ImportProfileId,
                        principalTable: "ImportProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_LinkedTransactionId",
                table: "Transactions",
                column: "LinkedTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_ImportProfileId",
                table: "Accounts",
                column: "ImportProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportProfiles_LedgerEntityId",
                table: "ImportProfiles",
                column: "LedgerEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportProfiles_Name_LedgerEntityId",
                table: "ImportProfiles",
                columns: new[] { "Name", "LedgerEntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportSkipRules_ImportProfileId",
                table: "ImportSkipRules",
                column: "ImportProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_Accounts_ImportProfiles_ImportProfileId",
                table: "Accounts",
                column: "ImportProfileId",
                principalTable: "ImportProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Transactions_LinkedTransactionId",
                table: "Transactions",
                column: "LinkedTransactionId",
                principalTable: "Transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Accounts_ImportProfiles_ImportProfileId",
                table: "Accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Transactions_LinkedTransactionId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "ImportSkipRules");

            migrationBuilder.DropTable(
                name: "ImportProfiles");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_LinkedTransactionId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_ImportProfileId",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "LinkedTransactionId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SuggestedSkipReason",
                table: "ImportItems");

            migrationBuilder.DropColumn(
                name: "ImportProfileId",
                table: "Accounts");
        }
    }
}
