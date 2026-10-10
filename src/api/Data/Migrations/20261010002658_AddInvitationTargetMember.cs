using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expenses.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvitationTargetMember : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MemberId",
                table: "Invitations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_MemberId",
                table: "Invitations",
                column: "MemberId");

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_Members_MemberId",
                table: "Invitations",
                column: "MemberId",
                principalTable: "Members",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invitations_Members_MemberId",
                table: "Invitations");

            migrationBuilder.DropIndex(
                name: "IX_Invitations_MemberId",
                table: "Invitations");

            migrationBuilder.DropColumn(
                name: "MemberId",
                table: "Invitations");
        }
    }
}
