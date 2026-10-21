
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QMSApplication.Migrations
{
    public partial class AddDepartmentNameToMDRecords : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DepartmentName",
                table: "MDRecords",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DepartmentName",
                table: "MDRecords");
        }
    }
}
