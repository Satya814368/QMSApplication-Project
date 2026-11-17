using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QMSApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddGuidelineUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ContentIsPlainText",
                table: "Guidelines",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TargetRoles",
                table: "Guidelines",
                type: "int",
                nullable: false,
                defaultValue: 31);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Guidelines",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Guidelines",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "GuidelineReadStatuses",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    GuidelineId = table.Column<long>(type: "bigint", nullable: false),
                    SeenVersion = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuidelineReadStatuses", x => new { x.UserId, x.GuidelineId });
                    table.ForeignKey(
                        name: "FK_GuidelineReadStatuses_Guidelines_GuidelineId",
                        column: x => x.GuidelineId,
                        principalTable: "Guidelines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GuidelineReadStatuses_PortalUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "PortalUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GuidelineReadStatuses_GuidelineId",
                table: "GuidelineReadStatuses",
                column: "GuidelineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuidelineReadStatuses");

            migrationBuilder.DropColumn(
                name: "ContentIsPlainText",
                table: "Guidelines");

            migrationBuilder.DropColumn(
                name: "TargetRoles",
                table: "Guidelines");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Guidelines");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Guidelines");
        }
    }
}
