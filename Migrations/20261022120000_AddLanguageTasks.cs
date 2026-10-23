using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace QMSApplication.Migrations;

public partial class AddLanguageTasks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DeliverableTasks",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                DeliverableId = table.Column<long>(type: "bigint", nullable: false),
                Language = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false).Annotation("MySql:CharSet", "utf8mb4"),
                AssignedUserId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                CreatedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false).Annotation("MySql:CharSet", "utf8mb4")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DeliverableTasks", x => x.Id);
                table.ForeignKey("FK_DeliverableTasks_MDRecords_DeliverableId", x => x.DeliverableId, "MDRecords", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_DeliverableTasks_PortalUsers_AssignedUserId", x => x.AssignedUserId, "PortalUsers", "Id", onDelete: ReferentialAction.Restrict);
            }).Annotation("MySql:CharSet", "utf8mb4");
        migrationBuilder.CreateTable(
            name: "DeliverableTargets",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                TaskId = table.Column<long>(type: "bigint", nullable: false),
                TargetNumber = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false).Annotation("MySql:CharSet", "utf8mb4"),
                AssignedUserId = table.Column<long>(type: "bigint", nullable: false),
                IsCancelled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                CancellationReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true).Annotation("MySql:CharSet", "utf8mb4"),
                GnApprovalReference = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true).Annotation("MySql:CharSet", "utf8mb4"),
                CancelledAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UpdatedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false).Annotation("MySql:CharSet", "utf8mb4"),
                ConcurrencyToken = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DeliverableTargets", x => x.Id);
                table.ForeignKey("FK_DeliverableTargets_DeliverableTasks_TaskId", x => x.TaskId, "DeliverableTasks", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_DeliverableTargets_PortalUsers_AssignedUserId", x => x.AssignedUserId, "PortalUsers", "Id", onDelete: ReferentialAction.Restrict);
            }).Annotation("MySql:CharSet", "utf8mb4");
        migrationBuilder.CreateTable(
            name: "TargetProgressSteps",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                TargetId = table.Column<long>(type: "bigint", nullable: false),
                StepNumber = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false).Annotation("MySql:CharSet", "utf8mb4"),
                MinutesSpent = table.Column<long>(type: "bigint", nullable: false),
                CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TargetProgressSteps", x => x.Id);
                table.ForeignKey("FK_TargetProgressSteps_DeliverableTargets_TargetId", x => x.TargetId, "DeliverableTargets", "Id", onDelete: ReferentialAction.Cascade);
            }).Annotation("MySql:CharSet", "utf8mb4");
        migrationBuilder.CreateIndex("IX_DeliverableTasks_AssignedUserId", "DeliverableTasks", "AssignedUserId");
        migrationBuilder.CreateIndex("IX_DeliverableTasks_DeliverableId_Language", "DeliverableTasks", new[] { "DeliverableId", "Language" }, unique: true);
        migrationBuilder.CreateIndex("IX_DeliverableTargets_AssignedUserId", "DeliverableTargets", "AssignedUserId");
        migrationBuilder.CreateIndex("IX_DeliverableTargets_TaskId_TargetNumber", "DeliverableTargets", new[] { "TaskId", "TargetNumber" }, unique: true);
        migrationBuilder.CreateIndex("IX_TargetProgressSteps_TargetId_StepNumber", "TargetProgressSteps", new[] { "TargetId", "StepNumber" }, unique: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("TargetProgressSteps");
        migrationBuilder.DropTable("DeliverableTargets");
        migrationBuilder.DropTable("DeliverableTasks");
    }
}
