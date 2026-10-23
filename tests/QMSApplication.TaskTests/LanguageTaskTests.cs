using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using QMSApplication.Data;
using QMSApplication.Models;
using QMSApplication.Pages;
using Xunit;

namespace QMSApplication.TaskTests;

public class LanguageTaskTests
{
    [Fact]
    public async Task LanguageCreatesSeparateTargetsWithExactlyFourteenSteps()
    {
        await using var fixture = await Fixture.Create();
        var result = await LanguageTaskFactory.BuildAsync(fixture.Db, JsonSerializer.Serialize(new[]
            { new { Language = "German", Targets = "0403024, 403025\n403026", AssignedUserId = fixture.Member.Id } }), "manager");
        Assert.Null(result.Error);
        var task = Assert.Single(result.Tasks);
        Assert.Equal(3, task.Targets.Count);
        Assert.Equal("0403024", task.Targets[0].TargetNumber);
        Assert.All(task.Targets, t =>
        {
            Assert.Equal(fixture.Member.Id, t.AssignedUserId);
            Assert.Equal(Enumerable.Range(1, 14), t.Steps.Select(x => x.StepNumber));
            Assert.All(t.Steps, s => Assert.Equal("Not Started", s.Status));
        });
    }

    [Theory]
    [InlineData("403024,403024")]
    [InlineData("403024;abc")]
    [InlineData("")]
    public async Task RejectsInvalidOrDuplicateTargets(string targets)
    {
        await using var f = await Fixture.Create();
        var result = await LanguageTaskFactory.BuildAsync(f.Db, JsonSerializer.Serialize(new[]
            { new { Language = "German", Targets = targets, AssignedUserId = f.Member.Id } }), "manager");
        Assert.NotNull(result.Error);
        Assert.Empty(result.Tasks);
    }

    [Fact]
    public async Task RejectsDuplicateLanguagesAndInactiveMembers()
    {
        await using var f = await Fixture.Create();
        var rows = new[]
        {
            new { Language = "German", Targets = "403024", AssignedUserId = f.Member.Id },
            new { Language = "german", Targets = "403025", AssignedUserId = f.Member.Id }
        };
        Assert.NotNull((await LanguageTaskFactory.BuildAsync(f.Db, JsonSerializer.Serialize(rows), "manager")).Error);
        f.Member.IsActive = false; await f.Db.SaveChangesAsync();
        Assert.NotNull((await LanguageTaskFactory.BuildAsync(f.Db, JsonSerializer.Serialize(rows.Take(1)), "manager")).Error);
    }

    [Fact]
    public void ProgressAndTimeDoNotTreatCancellationAsCompletion()
    {
        var target = new DeliverableTarget { Steps = Enumerable.Range(1, 14)
            .Select(i => new TargetProgressStep { StepNumber = i, Status = i <= 7 ? "Completed" : "Not Started" }).ToList() };
        Assert.Equal(50, TaskWorkflow.Percent(target));
        target.IsCancelled = true; Assert.Equal(0, TaskWorkflow.Percent(target));
        Assert.Equal("Cancelled with approval from GN", TaskWorkflow.CurrentStep(target));
        Assert.Equal(new long[] { 21, 20, 20 }, TaskWorkflow.SplitMinutes(61, 3));
        Assert.Equal(new long[] { 1, 0, 0 }, TaskWorkflow.SplitMinutes(1, 3));
    }

    [Fact]
    public async Task BulkUpdateDistributesTimeExactlyAndWritesAuditForEveryTarget()
    {
        await using var f = await Fixture.Create();
        var targets = await f.SeedTargets();
        var page = f.Page(f.Manager, targets);
        page.StepNumber = 4; page.StepStatus = "Completed"; page.Hours = 1; page.Minutes = 1;
        Assert.IsType<RedirectToPageResult>(await page.OnPostUpdateAsync(f.Deliverable.Id));
        f.Db.ChangeTracker.Clear();
        var steps = await f.Db.TargetProgressSteps.Where(x => x.StepNumber == 4).OrderBy(x => x.TargetId).ToListAsync();
        Assert.Equal(new long[] { 21, 20, 20 }, steps.Select(x => x.MinutesSpent));
        Assert.All(steps, s => Assert.Equal("Completed", s.Status));
        Assert.Equal(3, await f.Db.MDAuditLogs.CountAsync());
    }

    [Fact]
    public async Task StaleTokenRejectsWholeBulkUpdateWithoutAddingTime()
    {
        await using var f = await Fixture.Create();
        var targets = await f.SeedTargets();
        var stale = f.Page(f.Manager, targets);
        var first = f.Page(f.Manager, targets);
        first.Minutes = 9;
        await first.OnPostUpdateAsync(f.Deliverable.Id);
        f.Db.ChangeTracker.Clear();
        stale.Hours = 1; stale.Minutes = 40;
        Assert.IsType<PageResult>(await stale.OnPostUpdateAsync(f.Deliverable.Id));
        Assert.False(stale.ModelState.IsValid);
        Assert.Equal(9L, await f.Db.TargetProgressSteps.SumAsync(x => x.MinutesSpent));
        Assert.Equal(3, await f.Db.MDAuditLogs.CountAsync());
    }

    [Fact]
    public async Task MemberCannotUpdateAnotherMembersTarget()
    {
        await using var f = await Fixture.Create();
        var targets = await f.SeedTargets();
        targets[0].AssignedUserId = f.Manager.Id; await f.Db.SaveChangesAsync();
        var page = f.Page(f.Member, targets); page.Minutes = 30;
        var response = Assert.IsType<StatusCodeResult>(await page.OnPostUpdateAsync(f.Deliverable.Id));
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(0L, await f.Db.TargetProgressSteps.SumAsync(x => x.MinutesSpent));
    }

    [Fact]
    public async Task CancellationRequiresGnApprovalAndKeepsExistingHistory()
    {
        await using var f = await Fixture.Create();
        var targets = await f.SeedTargets();
        var page = f.Page(f.Manager, targets); page.UpdateAction = "Cancel"; page.CancellationReason = "Project withdrawn";
        Assert.IsType<PageResult>(await page.OnPostUpdateAsync(f.Deliverable.Id));
        Assert.False(page.ModelState.IsValid);
        Assert.False(await f.Db.DeliverableTargets.AnyAsync(x => x.IsCancelled));
        page = f.Page(f.Manager, targets); page.UpdateAction = "Cancel";
        page.CancellationReason = "Project withdrawn"; page.GnApprovalReference = "GN email 2026-10-22";
        Assert.IsType<RedirectToPageResult>(await page.OnPostUpdateAsync(f.Deliverable.Id));
        Assert.Equal(3, await f.Db.DeliverableTargets.CountAsync(x => x.IsCancelled));
        Assert.Equal(42, await f.Db.TargetProgressSteps.CountAsync());
    }

    [Fact]
    public async Task DatabaseEnforcesTargetUniquenessAndCascadesStepDeletion()
    {
        await using var f = await Fixture.Create();
        var targets = await f.SeedTargets();
        var duplicate = new DeliverableTarget { TaskId = targets[0].TaskId, TargetNumber = targets[0].TargetNumber,
            AssignedUserId = f.Member.Id, UpdatedBy = "manager" };
        f.Db.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        f.Db.Entry(duplicate).State = EntityState.Detached;
        f.Db.Remove(f.Deliverable); await f.Db.SaveChangesAsync();
        Assert.Equal(0, await f.Db.DeliverableTasks.CountAsync());
        Assert.Equal(0, await f.Db.DeliverableTargets.CountAsync());
        Assert.Equal(0, await f.Db.TargetProgressSteps.CountAsync());
    }

    [Fact]
    public void MySqlMigrationGeneratesOnlyTheThreeNewTables()
    {
        using var db = new QMSPortalDbContext(new DbContextOptionsBuilder<QMSPortalDbContext>()
            .UseMySql("Server=localhost;Database=TaskFeatureValidation;User=test;Password=test;",
                new MySqlServerVersion(new Version(8, 4, 0))).Options);
        var script = db.GetService<IMigrator>().GenerateScript("20261021092702_AddDepartmentNameToMDRecords", "20261022120000_AddLanguageTasks");
        Assert.Contains("CREATE TABLE `DeliverableTasks`", script);
        Assert.Contains("CREATE TABLE `DeliverableTargets`", script);
        Assert.Contains("CREATE TABLE `TargetProgressSteps`", script);
        Assert.DoesNotContain("ALTER TABLE `PortalUsers`", script);
        Assert.Contains("ON DELETE CASCADE", script);
    }

    [Fact]
    public async Task NextValidatesMandatoryDatesWithoutSavingDeliverable()
    {
        await using var f = await Fixture.Create();
        var page = f.CreatePage(f.Member);
        page.Input.MilestoneTargetDate = null;
        Assert.IsType<PageResult>(await page.OnPostNextAsync());
        Assert.False(page.ShowTasks);
        Assert.False(page.ModelState.IsValid);
        Assert.Equal(1, await f.Db.MDRecords.CountAsync());
        page = f.CreatePage(f.Member);
        Assert.IsType<PageResult>(await page.OnPostNextAsync());
        Assert.True(page.ShowTasks);
        Assert.Equal(1, await f.Db.MDRecords.CountAsync());
        Assert.Equal(0, await f.Db.DeliverableTasks.CountAsync());
    }

    [Fact]
    public async Task UserCanCreateDeliverableAndManageItsLanguageTasks()
    {
        await using var f = await Fixture.Create();
        var page = f.CreatePage(f.Member);
        var result = Assert.IsType<RedirectToPageResult>(await page.OnPostAsync());
        Assert.Equal("/MDTasks", result.PageName);
        var created = await f.Db.MDRecords.SingleAsync(x => x.RecordID == "MD-0002");
        Assert.Equal("member", created.CreatedBy);
        Assert.Equal("Not Started", created.Status);
        Assert.Equal("23-Oct-2026", created.MilestoneTargetDateSource);
        var tasks = await f.Db.DeliverableTasks.Include(x => x.Targets).ThenInclude(x => x.Steps)
            .Where(x => x.DeliverableId == created.Id).ToListAsync();
        Assert.Equal(2, Assert.Single(tasks).Targets.Count);
        Assert.Equal(28, tasks.SelectMany(x => x.Targets).SelectMany(x => x.Steps).Count());
        var tracker = f.Page(f.Member, tasks[0].Targets);
        Assert.IsType<PageResult>(await tracker.OnGetAsync(created.Id));
        Assert.True(tracker.CanManage);
    }

    [Fact]
    public async Task InvalidTaskDataDoesNotSaveAnyPartOfTheWizard()
    {
        await using var f = await Fixture.Create();
        var page = f.CreatePage(f.Member);
        page.TasksJson = JsonSerializer.Serialize(new[] { new { Language = "German", Targets = "403024,403024", AssignedUserId = f.Member.Id } });
        Assert.IsType<PageResult>(await page.OnPostAsync());
        Assert.False(page.ModelState.IsValid);
        Assert.Equal(1, await f.Db.MDRecords.CountAsync());
        Assert.Equal(0, await f.Db.DeliverableTasks.CountAsync());
        Assert.Equal(0, await f.Db.MDAuditLogs.CountAsync());
    }

    [Fact]
    public async Task BulkDeleteKeepsAuditHistoryAndCascadesLanguageData()
    {
        await using var f = await Fixture.Create();
        await f.SeedTargets();
        f.Manager.Role = "Admin"; await f.Db.SaveChangesAsync();
        var page = f.RecordsPage(f.Manager);
        page.SelectedRecordIds = new() { f.Deliverable.RecordID };
        Assert.IsType<RedirectToPageResult>(await page.OnPostBulkDeleteAsync());
        Assert.Equal(0, await f.Db.MDRecords.CountAsync());
        Assert.Equal(0, await f.Db.DeliverableTasks.CountAsync());
        Assert.Equal(0, await f.Db.DeliverableTargets.CountAsync());
        Assert.Equal(0, await f.Db.TargetProgressSteps.CountAsync());
        var audit = await f.Db.MDAuditLogs.SingleAsync();
        Assert.Equal("Delete", audit.Action);
        Assert.Contains("403024", audit.OldValue!);
        Assert.Contains("StepNumber", audit.OldValue!);
    }

    [Fact]
    public async Task UserCannotBulkDeleteDeliverables()
    {
        await using var f = await Fixture.Create();
        await f.SeedTargets();
        var page = f.RecordsPage(f.Member);
        page.SelectedRecordIds = new() { f.Deliverable.RecordID };
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await page.OnPostBulkDeleteAsync()).StatusCode);
        Assert.Equal(1, await f.Db.MDRecords.CountAsync());
        Assert.Equal(42, await f.Db.TargetProgressSteps.CountAsync());
    }

    [Fact]
    public async Task LatestDateDisplayAndBulkSelectionRemainAvailableWithTaskCounts()
    {
        await using var f = await Fixture.Create();
        await f.SeedTargets();
        f.Deliverable.MilestoneTargetDate = new DateOnly(2026, 10, 23);
        f.Deliverable.DeliverableTargetDate = new DateOnly(2026, 10, 25);
        await f.Db.SaveChangesAsync();
        var page = f.RecordsPage(f.Manager);
        page.OnGet();
        var record = Assert.Single(page.Records);
        Assert.Equal("23-10-2026", record.MilestoneTargetDate);
        Assert.Equal("25-10-2026", record.DeliverableTargetDate);
        Assert.Equal(1, record.TaskCount);
        Assert.Equal(f.Deliverable.Id, record.DeliverableId);
        Assert.NotNull(page.SelectedRecordIds);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public QMSPortalDbContext Db { get; }
        public PortalUser Manager { get; private set; } = null!;
        public PortalUser Member { get; private set; } = null!;
        public MDEntity Deliverable { get; private set; } = null!;
        private Fixture(SqliteConnection connection, QMSPortalDbContext db) { _connection = connection; Db = db; }
        public static async Task<Fixture> Create()
        {
            var conn = new SqliteConnection("Data Source=:memory:"); await conn.OpenAsync();
            // The production GUID column specifies MySQL's ASCII collation.
            conn.CreateCollation("ascii_general_ci", (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
            var db = new QMSPortalDbContext(new DbContextOptionsBuilder<QMSPortalDbContext>().UseSqlite(conn).Options);
            await db.Database.EnsureCreatedAsync();
            var fixture = new Fixture(conn, db)
            {
                Manager = new PortalUser { Username = "manager", FullName = "Manager", Role = "Manager" },
                Member = new PortalUser { Username = "member", FullName = "Member", Role = "User" },
                Deliverable = new MDEntity { RecordID = "MD-0001", DeliverableName = "UG", MilestoneName = "Release" }
            };
            db.AddRange(fixture.Manager, fixture.Member, fixture.Deliverable); await db.SaveChangesAsync();
            return fixture;
        }
        public async Task<List<DeliverableTarget>> SeedTargets()
        {
            var result = await LanguageTaskFactory.BuildAsync(Db, JsonSerializer.Serialize(new[]
                { new { Language = "German", Targets = "403024,403025,403026", AssignedUserId = Member.Id } }), Manager.Username);
            result.Tasks[0].DeliverableId = Deliverable.Id; Db.AddRange(result.Tasks); await Db.SaveChangesAsync();
            return result.Tasks[0].Targets;
        }
        public MDTasksModel Page(PortalUser actor, List<DeliverableTarget> targets)
        {
            var http = new DefaultHttpContext { Session = new TestSession() };
            http.Session.SetString("Username", actor.Username);
            return new MDTasksModel(Db)
            {
                PageContext = new PageContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new TestTempDataProvider()),
                SelectedTargets = targets.Select(x => x.Id).ToList(),
                Versions = targets.ToDictionary(x => x.Id, x => x.ConcurrencyToken)
            };
        }
        public MDCreateModel CreatePage(PortalUser actor)
        {
            var http = new DefaultHttpContext { Session = new TestSession() };
            http.Session.SetString("Username", actor.Username);
            return new MDCreateModel(Db)
            {
                PageContext = new PageContext { HttpContext = http }, TempData = new TempDataDictionary(http, new TestTempDataProvider()),
                Input = new MDCreateModel.InputModel
                {
                    MilestoneName = "New release", DeliverableName = "New UG", DepartmentName = "Labeling",
                    DeliverableType = "UGs", AssignedTo = actor.Username,
                    MilestoneTargetDate = new DateOnly(2026, 10, 23), DeliverableTargetDate = new DateOnly(2026, 10, 25)
                },
                TasksJson = JsonSerializer.Serialize(new[] { new { Language = "German", Targets = "403024,403025", AssignedUserId = actor.Id } })
            };
        }
        public MDModel RecordsPage(PortalUser actor)
        {
            var http = new DefaultHttpContext { Session = new TestSession() };
            http.Session.SetString("Username", actor.Username);
            return new MDModel(Db)
            {
                PageContext = new PageContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new TestTempDataProvider())
            };
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
    }
    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new();
        public bool IsAvailable => true; public string Id => "test"; public IEnumerable<string> Keys => _values.Keys;
        public void Clear() => _values.Clear(); public void Remove(string key) => _values.Remove(key);
        public void Set(string key, byte[] value) => _values[key] = value;
        public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? value) => _values.TryGetValue(key, out value);
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
