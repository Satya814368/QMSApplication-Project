using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QMSApplication.Migrations;

public partial class AddNotificationRevisions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "Revision", table: "Notifications", type: "int", nullable: true);
        migrationBuilder.AddColumn<int>(name: "Revision", table: "NotificationUserStatuses", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.CreateIndex(
            name: "IX_NotificationUserStatuses_NotificationId_Username_Revision",
            table: "NotificationUserStatuses",
            columns: new[] { "NotificationId", "Username", "Revision" });
        migrationBuilder.Sql("""
            UPDATE Notifications AS n SET Revision = 0
            WHERE IsPublished = 1 OR EXISTS
                (SELECT 1 FROM NotificationUserStatuses AS s WHERE s.NotificationId = n.Id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep only status rows for the final published version when removing version support.
        migrationBuilder.Sql("""
            DELETE FROM NotificationUserStatuses
            WHERE EXISTS (SELECT 1 FROM Notifications AS n
                WHERE n.Id = NotificationUserStatuses.NotificationId
                    AND NotificationUserStatuses.Revision <> COALESCE(n.Revision, 0));
            """);
        migrationBuilder.DropIndex(name: "IX_NotificationUserStatuses_NotificationId_Username_Revision", table: "NotificationUserStatuses");
        migrationBuilder.DropColumn(name: "Revision", table: "NotificationUserStatuses");
        migrationBuilder.DropColumn(name: "Revision", table: "Notifications");
    }
}
