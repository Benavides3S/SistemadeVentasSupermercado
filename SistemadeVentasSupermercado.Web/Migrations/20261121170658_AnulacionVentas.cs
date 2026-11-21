using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemadeVentasSupermercado.Web.Migrations
{
    /// <inheritdoc />
    public partial class AnulacionVentas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelledByUserId",
                table: "Sales",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CancelledByUserId",
                table: "Sales",
                column: "CancelledByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_AspNetUsers_CancelledByUserId",
                table: "Sales",
                column: "CancelledByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sales_AspNetUsers_CancelledByUserId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_CancelledByUserId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "Sales");
        }
    }
}
