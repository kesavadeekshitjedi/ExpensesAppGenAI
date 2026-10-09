using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expenses.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VehicleId",
                table: "LineItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Make = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Year = table.Column<int>(type: "int", nullable: true),
                    Archived = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LineItems_VehicleId",
                table: "LineItems",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_HouseholdId_Name",
                table: "Vehicles",
                columns: new[] { "HouseholdId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LineItems_Vehicles_VehicleId",
                table: "LineItems",
                column: "VehicleId",
                principalTable: "Vehicles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LineItems_Vehicles_VehicleId",
                table: "LineItems");

            migrationBuilder.DropTable(
                name: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_LineItems_VehicleId",
                table: "LineItems");

            migrationBuilder.DropColumn(
                name: "VehicleId",
                table: "LineItems");
        }
    }
}
