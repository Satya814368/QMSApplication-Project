using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Data;
using QMSApplication.Models;
using QMSApplication.Security;
using Xunit;

namespace QMSApplication.TaskTests;

public class HistoricalMDImportTests
{
    [Fact]
    public async Task HistoricalImportKeepsNamesAndOriginalCellsWithoutCreatingAssignments()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateCollation("ascii_general_ci", (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
        await using var db = new QMSPortalDbContext(new DbContextOptionsBuilder<QMSPortalDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var team = new Team { Name = "Labelling", NormalizedName = "LABELLING" };
        var admin = new PortalUser { Username = "admin", Role = "Admin", IsActive = true, IsApproved = true };
        var former = new PortalUser { Username = "former", Role = "User", IsActive = false, IsApproved = true };
        var current = new PortalUser { Username = "current", Role = "User", IsActive = true, IsApproved = true };
        db.AddRange(team, admin, former, current);
        await db.SaveChangesAsync();
        db.TeamMemberships.AddRange(new TeamMembership { UserId = current.Id, TeamId = team.Id },
            new TeamMembership { UserId = former.Id, TeamId = team.Id });
        await db.SaveChangesAsync();

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Archive");
        sheet.Cell("A1").Value = "S.No";
        sheet.Cell("G1").Value = "Assigned To";
        sheet.Cell("Q1").Value = "Notes from former team";
        sheet.Cell("A2").Value = "1";
        sheet.Cell("G2").Value = "former";
        sheet.Cell("G3").Value = "No Dashboard account";
        sheet.Cell("G4").Value = "current";
        sheet.Cell("Q5").Value = "Only this extra column is filled";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        var file = new FormFile(stream, 0, stream.Length, "ExcelFile", "archive.xlsx");

        var importer = new MDExcelImporter(db);
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            Assert.Equal(4, await importer.ImportAsync(file, team.Id, admin.Username, historicalImport: true));
            await transaction.CommitAsync();
        }
        var records = await db.MDRecords.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(new[] { "former", "No Dashboard account", "current", "NA" }, records.Select(r => r.SourceAssignedTo));
        Assert.All(records, r =>
        {
            Assert.Null(r.AssignedTo);
            Assert.Equal("NA", r.MilestoneName);
            Assert.Equal("NA", r.DeliverableName);
            Assert.Equal("NA", r.MilestoneTargetDateSource);
            Assert.Null(r.MilestoneTargetDate);
        });
        var original = Assert.Single(JsonSerializer.Deserialize<List<MDExcelImporter.SourceCell>>(records[3].ImportSourceJson!)!);
        Assert.Equal(new MDExcelImporter.SourceCell(17, "Notes from former team", "Only this extra column is filled"), original);
        Assert.Empty(await new AccessService(db).Records(current).ToListAsync());
        Assert.Empty(await db.DeliverableTasks.ToListAsync());

        stream.Position = 0;
        await using var liveTransaction = await db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => importer.ImportAsync(file, team.Id, admin.Username));
        await liveTransaction.RollbackAsync();
    }
}
