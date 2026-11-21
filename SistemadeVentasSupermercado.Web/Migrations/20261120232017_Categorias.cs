using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemadeVentasSupermercado.Web.Migrations
{
    /// <inheritdoc />
    public partial class Categorias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true);

            // 1) Columna nueva, opcional por ahora
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "Products",
                type: "uniqueidentifier",
                nullable: true);

            // 2) Una categoría por cada texto distinto que ya existe
            migrationBuilder.Sql(@"
        INSERT INTO Categories (Id, Name, IsActive)
        SELECT NEWID(), x.Name, 1
        FROM (SELECT DISTINCT LTRIM(RTRIM(Category)) AS Name
              FROM Products
              WHERE LTRIM(RTRIM(Category)) <> '') x;");

            // 3) Por si algún producto tuviera la categoría vacía
            migrationBuilder.Sql(@"
        IF EXISTS (SELECT 1 FROM Products WHERE Category IS NULL OR LTRIM(RTRIM(Category)) = '')
           AND NOT EXISTS (SELECT 1 FROM Categories WHERE Name = N'Sin categoría')
            INSERT INTO Categories (Id, Name, IsActive) VALUES (NEWID(), N'Sin categoría', 1);");

            // 4) Enlazar cada producto con su categoría
            migrationBuilder.Sql(@"
        UPDATE p SET p.CategoryId = c.Id
        FROM Products p
        INNER JOIN Categories c ON c.Name = LTRIM(RTRIM(p.Category));");

            migrationBuilder.Sql(@"
        UPDATE p SET p.CategoryId = c.Id
        FROM Products p
        INNER JOIN Categories c ON c.Name = N'Sin categoría'
        WHERE p.CategoryId IS NULL;");

            // 5) Ahora sí obligatoria, con su relación
            migrationBuilder.AlterColumn<Guid>(
                name: "CategoryId",
                table: "Products",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId",
                table: "Products",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Categories_CategoryId",
                table: "Products",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // 6) Se elimina el texto viejo, ya convertido
            migrationBuilder.DropColumn(
                name: "Category",
                table: "Products");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Categories_CategoryId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Products_CategoryId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Products");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Products",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");
        }
    }
}
