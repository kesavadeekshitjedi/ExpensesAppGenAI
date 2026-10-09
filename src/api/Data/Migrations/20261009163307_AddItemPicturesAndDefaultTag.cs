using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expenses.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddItemPicturesAndDefaultTag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultValueTagId",
                table: "Items",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PictureBlobName",
                table: "Items",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_DefaultValueTagId",
                table: "Items",
                column: "DefaultValueTagId");

            migrationBuilder.AddForeignKey(
                name: "FK_Items_ValueTags_DefaultValueTagId",
                table: "Items",
                column: "DefaultValueTagId",
                principalTable: "ValueTags",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Items_ValueTags_DefaultValueTagId",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Items_DefaultValueTagId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "DefaultValueTagId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "PictureBlobName",
                table: "Items");
        }
    }
}
