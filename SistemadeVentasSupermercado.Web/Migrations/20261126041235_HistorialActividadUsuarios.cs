using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemadeVentasSupermercado.Web.Migrations
{
    /// <inheritdoc />
    public partial class HistorialActividadUsuarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    TargetUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    TargetName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GroupKey = table.Column<string>(type: "nvarchar(350)", maxLength: 350, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserActivities", x => x.Id);
                    table.CheckConstraint("CK_UserActivities_AttemptCount_Positive", "[AttemptCount] >= 1");
                    table.ForeignKey(
                        name: "FK_UserActivities_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserActivities_AspNetUsers_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserActivities_ActorUserId",
                table: "UserActivities",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserActivities_GroupKey",
                table: "UserActivities",
                column: "GroupKey",
                unique: true,
                filter: "[GroupKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserActivities_Number",
                table: "UserActivities",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserActivities_OccurredAtUtc",
                table: "UserActivities",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UserActivities_TargetUserId",
                table: "UserActivities",
                column: "TargetUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserActivities");
        }
    }
}
