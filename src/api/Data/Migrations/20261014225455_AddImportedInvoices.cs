using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expenses.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddImportedInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportedInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Vendor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OrderNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PurchaseDate = table.Column<DateOnly>(type: "date", nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BlobName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportedInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportedInvoices_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedInvoices_ExpenseId",
                table: "ImportedInvoices",
                column: "ExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportedInvoices_HouseholdId_ContentHash",
                table: "ImportedInvoices",
                columns: new[] { "HouseholdId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedInvoices_HouseholdId_OrderNumber",
                table: "ImportedInvoices",
                columns: new[] { "HouseholdId", "OrderNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportedInvoices");
        }
    }
}
