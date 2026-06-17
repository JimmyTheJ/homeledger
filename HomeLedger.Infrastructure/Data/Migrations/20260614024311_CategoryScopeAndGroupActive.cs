using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeLedger.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CategoryScopeAndGroupActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Categories_Name",
                table: "Categories");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "CategoryGroups",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LedgerEntityId",
                table: "CategoryGroups",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LedgerEntityId",
                table: "Categories",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CategoryGroups_LedgerEntityId",
                table: "CategoryGroups",
                column: "LedgerEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryGroups_Name_LedgerEntityId",
                table: "CategoryGroups",
                columns: new[] { "Name", "LedgerEntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_LedgerEntityId",
                table: "Categories",
                column: "LedgerEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Name_LedgerEntityId",
                table: "Categories",
                columns: new[] { "Name", "LedgerEntityId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Categories_Entities_LedgerEntityId",
                table: "Categories",
                column: "LedgerEntityId",
                principalTable: "Entities",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CategoryGroups_Entities_LedgerEntityId",
                table: "CategoryGroups",
                column: "LedgerEntityId",
                principalTable: "Entities",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Categories_Entities_LedgerEntityId",
                table: "Categories");

            migrationBuilder.DropForeignKey(
                name: "FK_CategoryGroups_Entities_LedgerEntityId",
                table: "CategoryGroups");

            migrationBuilder.DropIndex(
                name: "IX_CategoryGroups_LedgerEntityId",
                table: "CategoryGroups");

            migrationBuilder.DropIndex(
                name: "IX_CategoryGroups_Name_LedgerEntityId",
                table: "CategoryGroups");

            migrationBuilder.DropIndex(
                name: "IX_Categories_LedgerEntityId",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Categories_Name_LedgerEntityId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "CategoryGroups");

            migrationBuilder.DropColumn(
                name: "LedgerEntityId",
                table: "CategoryGroups");

            migrationBuilder.DropColumn(
                name: "LedgerEntityId",
                table: "Categories");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true);
        }
    }
}
