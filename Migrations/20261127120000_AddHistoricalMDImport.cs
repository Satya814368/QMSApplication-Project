using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QMSApplication.Migrations;

public partial class AddHistoricalMDImport : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "SourceAssignedTo", table: "MDRecords", type: "longtext", nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");
        migrationBuilder.AddColumn<string>(name: "ImportSourceJson", table: "MDRecords", type: "longtext", nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ImportSourceJson", table: "MDRecords");
        migrationBuilder.DropColumn(name: "SourceAssignedTo", table: "MDRecords");
    }
}
