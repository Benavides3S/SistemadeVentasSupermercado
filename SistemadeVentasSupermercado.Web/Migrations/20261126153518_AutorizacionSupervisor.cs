using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemadeVentasSupermercado.Web.Migrations
{
    /// <inheritdoc />
    public partial class AutorizacionSupervisor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelAuthorizedByUserId",
                table: "Sales",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorizedByUserId",
                table: "SaleReturns",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserAuthorizationPins",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PinHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAuthorizationPins", x => x.UserId);
                    table.CheckConstraint("CK_UserAuthorizationPins_FailedAttempts_NonNegative", "[FailedAttempts] >= 0");
                    table.ForeignKey(
                        name: "FK_UserAuthorizationPins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CancelAuthorizedByUserId",
                table: "Sales",
                column: "CancelAuthorizedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturns_AuthorizedByUserId",
                table: "SaleReturns",
                column: "AuthorizedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_SaleReturns_AspNetUsers_AuthorizedByUserId",
                table: "SaleReturns",
                column: "AuthorizedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_AspNetUsers_CancelAuthorizedByUserId",
                table: "Sales",
                column: "CancelAuthorizedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SaleReturns_AspNetUsers_AuthorizedByUserId",
                table: "SaleReturns");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_AspNetUsers_CancelAuthorizedByUserId",
                table: "Sales");

            migrationBuilder.DropTable(
                name: "UserAuthorizationPins");

            migrationBuilder.DropIndex(
                name: "IX_Sales_CancelAuthorizedByUserId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_SaleReturns_AuthorizedByUserId",
                table: "SaleReturns");

            migrationBuilder.DropColumn(
                name: "CancelAuthorizedByUserId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "AuthorizedByUserId",
                table: "SaleReturns");
        }
    }
}
