using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Data;
using QMSApplication.Models;
using QMSApplication.Pages;
using QMSApplication.Security;
using Xunit;

namespace QMSApplication.TaskTests;
public class RoleAccessTests
{
    [Theory]
    [InlineData("admin", 4)]
    [InlineData("manager", 1)]
    [InlineData("lead", 1)]
    [InlineData("member", 2)]
    [InlineData("guest", 4)]
    [InlineData("outsider", 0)]
    public async Task EachRoleHasTheAgreedRecordScope(string name, int count)
    {
        await using var f = await Fixture.Create();
        Assert.Equal(count, await new AccessService(f.Db).Records(f.User(name)).CountAsync());
    }

    [Fact]
    public async Task DepartmentScopeMustBeExplicitAndNeverCrossDepartments()
    {
        await using var f = await Fixture.Create();
        var manager = f.User("manager");
        var access = new AccessService(f.Db);
        Assert.Single(await access.Records(manager).ToListAsync());
        manager.ManagerDepartmentScope = true;
        await f.Db.SaveChangesAsync();
        Assert.Equal(2, await access.Records(manager).CountAsync());
        Assert.DoesNotContain(await access.Records(manager).ToListAsync(), x => x.RecordID == "MD-C");
    }

    [Fact]
    public async Task CreatorAndDeliverableAssignmentDoNotGrantTaskManagement()
    {
        await using var f = await Fixture.Create();
        var actor = f.User("member");
        var access = new AccessService(f.Db);
        var record = await f.Db.MDRecords.SingleAsync(x => x.RecordID == "MD-A");
        record.CreatedBy = actor.Username;
        await f.Db.SaveChangesAsync();
        Assert.False(await access.Manage(actor, record));
        Assert.False(await access.Delete(actor, record));
        var page = f.Page(new MDTasksModel(f.Db), actor);
        Assert.IsType<PageResult>(await page.OnGetAsync(record.Id));
        Assert.False(page.CanManage);
        Assert.All(page.Tasks.SelectMany(x => x.Targets), x => Assert.Equal(actor.Id, x.AssignedUserId));
        page.TasksJson = "[]";
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await page.OnPostAddAsync(record.Id)).StatusCode);
    }

    [Fact]
    public async Task ManagerCannotForgeAnotherTeamsDeliverableId()
    {
        await using var f = await Fixture.Create();
        var record = await f.Db.MDRecords.SingleAsync(x => x.RecordID == "MD-C");
        var tasks = f.Page(new MDTasksModel(f.Db), f.User("manager"));
        Assert.Equal("/AccessDenied", Assert.IsType<RedirectToPageResult>(await tasks.OnGetAsync(record.Id)).PageName);
        var edit = f.Page(new MDEditModel(f.Db) { Id = record.RecordID }, f.User("manager"));
        Assert.Equal("/AccessDenied", Assert.IsType<RedirectToPageResult>(await edit.OnGetAsync()).PageName);
        Assert.False(await new AccessService(f.Db).Delete(f.User("manager"), record));
    }

    [Fact]
    public async Task TeamLeadCanEditButCannotDeleteAndUserCannotCreate()
    {
        await using var f = await Fixture.Create();
        var record = await f.Db.MDRecords.SingleAsync(x => x.RecordID == "MD-A");
        var access = new AccessService(f.Db);
        Assert.True(await access.Manage(f.User("lead"), record));
        Assert.False(await access.Delete(f.User("lead"), record));
        Assert.True(await access.Delete(f.User("manager"), record));
        var create = f.Page(new MDCreateModel(f.Db), f.User("member"));
        Assert.Equal("/AccessDenied", Assert.IsType<RedirectToPageResult>(await create.OnGetAsync()).PageName);
    }

    [Fact]
    public async Task SearchDashboardReportsAndMyTasksAgreeOnAssignedScope()
    {
        await using var f = await Fixture.Create();
        var actor = f.User("member");
        var search = f.Page(new GlobalSearchModel(f.Db) { Query = "Release" }, actor);
        await search.OnGetAsync();
        Assert.Equal(2, search.Results.Count);
        var report = f.Page(new ReportsModel(f.Db), actor);
        await report.OnGetAsync();
        Assert.Equal(2, report.Total);
        var dashboard = f.Page(new DashboardModel(f.Db), actor);
        await dashboard.OnGetAsync(null);
        Assert.Equal(2, dashboard.CompletedCount);
        var my = f.Page(new MyTasksModel(f.Db), actor);
        await my.OnGetAsync();
        Assert.Equal(2, my.Deliverables.Count);
        Assert.Equal(2, my.TotalTargets);
        var completed = f.Page(new CompletedUGModel(f.Db), actor);
        await completed.OnGetAsync();
        Assert.Equal(2, completed.MilestoneCounts.Sum());
        Assert.DoesNotContain(search.Results, x => x.RecordID == "MD-C");
    }

    [Fact]
    public async Task LegacyFullNameAssignmentIsConsistent()
    {
        await using var f = await Fixture.Create();
        var record = await f.Db.MDRecords.SingleAsync(x => x.RecordID == "MD-D");
        record.AssignedTo = f.User("outsider").FullName;
        await f.Db.SaveChangesAsync();
        var my = f.Page(new MyTasksModel(f.Db), f.User("outsider"));
        await my.OnGetAsync();
        Assert.Equal("MD-D", Assert.Single(my.Deliverables).Record.RecordID);
        var records = f.Page(new MDModel(f.Db), f.User("outsider"));
        records.OnGet();
        Assert.Equal("MD-D", Assert.Single(records.Records).RecordID);
    }

    [Fact]
    public async Task StaleOrInactiveSessionsLoseAccessAndRoleIsReloaded()
    {
        await using var f = await Fixture.Create();
        var user = f.User("member");
        var http = f.Http(user);
        http.Session.SetString("UserRole", "Admin");
        Assert.NotNull(await new AccessService(f.Db).Current(http));
        Assert.Equal("User", http.Session.GetString("UserRole"));
        var old = f.Http(user);
        user.SecurityStamp = Guid.NewGuid();
        await f.Db.SaveChangesAsync();
        Assert.Null(await new AccessService(f.Db).Current(old));
        user.IsActive = false;
        await f.Db.SaveChangesAsync();
        Assert.Null(await new AccessService(f.Db).Current(f.Http(user)));
    }

    [Theory]
    [InlineData("manager", "/ManageUsers", null)]
    [InlineData("lead", "/ManageUsers", null)]
    [InlineData("guest", "/MDTasks", "Update")]
    [InlineData("guest", "/ManageUsers", "Save")]
    [InlineData("member", "/ImportUsers", null)]
    [InlineData("member", "/MDCreate", null)]
    public async Task GlobalFilterRejectsForbiddenRoutesAndForgedPosts(string user, string route, string? handler)
    {
        await using var f = await Fixture.Create();
        var http = f.Http(f.User(user));
        http.Request.Method = handler == null ? "GET" : "POST";
        var ctx = new PageContext
        {
            HttpContext = http,
            RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            ActionDescriptor = new CompiledPageActionDescriptor
            {
                ViewEnginePath = route
            }
        };
        var filters = new List<IFilterMetadata>();
        var descriptor = new HandlerMethodDescriptor
        {
            Name = handler
        };
        var executing = new PageHandlerExecutingContext(ctx, filters, descriptor, new Dictionary<string, object?>(), this);
        bool ran = false;
        await new AccessPageFilter(new AccessService(f.Db)).OnPageHandlerExecutionAsync(executing, () =>
        {
            ran = true;
            return Task.FromResult(new PageHandlerExecutedContext(ctx, filters, descriptor, this));
        });
        Assert.False(ran);
        Assert.Equal("/AccessDenied", Assert.IsType<RedirectToPageResult>(executing.Result).PageName);
    }

    [Fact]
    public async Task AnonymousSearchRedirectsBeforeQuerying()
    {
        await using var f = await Fixture.Create();
        var page = f.Page(new GlobalSearchModel(f.Db) { Query = "Release" }, null);
        Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(await page.OnGetAsync()).PageName);
        Assert.Empty(page.Results);
    }

    [Fact]
    public async Task LastActiveAdminCannotBeDemotedOrDisabled()
    {
        await using var f = await Fixture.Create();
        var admin = f.User("admin");
        var accounts = new AccountService(f.Db);
        Assert.NotNull(await accounts.Change(admin.Username, admin.Id, admin.Version, "User", true, true, null, false, "test"));
        Assert.NotNull(await accounts.Change(admin.Username, admin.Id, admin.Version, "Admin", true, false, null, false, "test"));
        Assert.Equal("Admin", admin.Role);
        Assert.True(admin.IsActive);
        Assert.NotNull(await accounts.Change("member", admin.Id, admin.Version, "Guest", true, true, null, false, "forged"));
    }

    [Fact]
    public async Task ApprovalRoleChangesInvalidateSessionsAndPreserveAudit()
    {
        await using var f = await Fixture.Create();
        var user = f.User("member");
        var http = f.Http(user);
        var version = user.Version;
        Assert.Null(await new AccountService(f.Db).Change("admin", user.Id, version, "Guest", true, true, null, false, "role review"));
        Assert.Empty(await f.Db.TeamMemberships.Where(x => x.UserId == user.Id).ToListAsync());
        Assert.Null(await new AccessService(f.Db).Current(http));
        Assert.NotNull(await new AccountService(f.Db).Change("admin", user.Id, version, "User", true, true, null, false, "stale"));
        Assert.Contains(await f.Db.TeamAuditEvents.ToListAsync(), x => x.Action == "Account permissions changed" && x.DetailsJson.Contains("role review"));
    }

    [Fact]
    public async Task LegacyResetLinksCannotBeIssuedOrUsed()
    {
        await using var f = await Fixture.Create();var accounts=new AccountService(f.Db);var user=f.User("member");
        var oldHash=user.PasswordHash;
        f.Db.PasswordResetRequests.Add(new PasswordResetRequest { UserId=user.Id,TokenHash=AccountService.Digest("old-link"),ExpiresAtUtc=DateTime.UtcNow.AddHours(1) });await f.Db.SaveChangesAsync();
        Assert.Null((await accounts.IssueReset("admin",user.Id)).Token);
        Assert.Null((await accounts.IssueReset("member",user.Id)).Token);
        Assert.NotNull(await accounts.Reset("old-link","NewPassword12345!"));Assert.Equal(oldHash,user.PasswordHash);
    }

    [Fact]
    public async Task NewRegistrationIsPendingUntilAdminApprovesIt()
    {
        await using var f = await Fixture.Create();
        var delivery = new TestRegistrationEmailSender();
        var otp = new RegistrationOtpService(f.Db, delivery, TimeProvider.System);
        var page = f.Page(new RegisterModel(otp) { FirstName = "New", LastName = "Person", Username = "newperson", Email = "new@example.test", MobileNumber = "123456789", Password = "NewPassword12345!" }, null);
        Assert.Equal("/VerifyRegistration", Assert.IsType<RedirectToPageResult>(await page.OnPostAsync()).PageName);
        Assert.False(await f.Db.PortalUsers.AnyAsync(x => x.Username == "newperson"));
        var challenge = Guid.Parse(page.HttpContext.Session.GetString(RegistrationOtpService.SessionChallenge)!);
        var owner = page.HttpContext.Session.GetString(RegistrationOtpService.SessionOwner)!;
        Assert.Null(await otp.Verify(challenge, owner, delivery.Messages.Single().Code));
        var user = await f.Db.PortalUsers.SingleAsync(x => x.Username == "newperson");
        Assert.False(user.IsApproved);
        Assert.False(user.IsActive);
        Assert.Equal("User", user.Role);
        var login = f.Page(new LoginModel(f.Db) { Username = user.Username, Password = "NewPassword12345!" }, null);
        Assert.IsType<PageResult>(await login.OnPostAsync());
        Assert.Null(await new AccountService(f.Db).Change("admin", user.Id, user.Version, "User", true, true, null, false, "approved"));
        Assert.IsType<RedirectToPageResult>(await login.OnPostAsync());
        Assert.NotNull(login.HttpContext.Session.GetString("SecurityStamp"));
    }

    [Fact]
    public async Task TeamDirectoryIncludesUnassignedAndOtherTeamMembersAndLeadCanTransfer()
    {
        await using var f = await Fixture.Create();
        var page = f.Page(new MyTeamModel(f.Db, new TeamSetupService(f.Db)), f.User("lead"));
        await page.OnGetAsync();
        Assert.Contains(page.Users, x => x.Username == "outsider");
        Assert.Contains(page.Users, x => x.Username == "other");
        var team = Assert.Single(page.Teams);
        var other = f.User("other");
        var original = await f.Db.TeamMemberships.SingleAsync(x => x.UserId == other.Id);
        Assert.Null(await new TeamSetupService(f.Db).MoveMember("lead", other.Id, team.Id, original.TeamId, "Load balance"));
        Assert.NotNull(await new TeamSetupService(f.Db).MoveMember("lead", other.Id, team.Id, null, "stale"));
    }

    [Fact]
    public async Task ManagerCreatesOwnTeamAndCanReplaceLeadButNotAnotherTeamsLead()
    {
        await using var f = await Fixture.Create();
        var setup = new TeamSetupService(f.Db);
        Assert.Null(await setup.Create("manager", "New team"));
        var created = await f.Db.Teams.SingleAsync(x => x.Name == "New team");
        Assert.True(await f.Db.TeamManagers.AnyAsync(x => x.TeamId == created.Id && x.UserId == f.User("manager").Id));
        var own = await f.Db.Teams.SingleAsync(x => x.Name == "A");
        Assert.Null(await setup.SetLead("manager", own.Id, null, own.Version));
        Assert.Null(await setup.SetLead("manager", own.Id, f.User("lead").Id, own.Version));
        var other = await f.Db.Teams.SingleAsync(x => x.Name == "C");
        Assert.NotNull(await setup.SetLead("manager", other.Id, f.User("lead").Id, other.Version));
    }

    [Fact]
    public async Task AssignmentsMustBelongToOwningTeamAndBeApproved()
    {
        await using var f = await Fixture.Create();
        var access = new AccessService(f.Db);
        var team = await f.Db.Teams.SingleAsync(x => x.Name == "A");
        var task = new DeliverableTask
        {
            AssignedUserId = f.User("other").Id
        };
        Assert.False(await access.ValidAssignments(team.Id, "member", new[] { task }));
        task.AssignedUserId = f.User("member").Id;
        Assert.True(await access.ValidAssignments(team.Id, "member", new[] { task }));
        f.User("member").IsApproved = false;
        await f.Db.SaveChangesAsync();
        Assert.False(await access.ValidAssignments(team.Id, "member", new[] { task }));
    }

    [Fact]
    public async Task ImportUsersGetNeverWritesAndDoesNotAcceptNonAdmin()
    {
        await using var f = await Fixture.Create();
        var count = await f.Db.PortalUsers.CountAsync();
        var admin = f.Page(new ImportUsersModel(f.Db), f.User("admin"));
        Assert.IsType<PageResult>(await admin.OnGetAsync());
        Assert.Equal(count, await f.Db.PortalUsers.CountAsync());
        var member = f.Page(new ImportUsersModel(f.Db), f.User("member"));
        Assert.Equal("/AccessDenied", Assert.IsType<RedirectToPageResult>(await member.OnPostAsync()).PageName);
    }

    [Fact]
    public async Task UnknownRolesFailClosedAndPendingAccountsCannotReceiveTasks()
    {
        await using var f = await Fixture.Create();
        var user = f.User("member"); user.Role = "UnexpectedRole"; await f.Db.SaveChangesAsync();
        Assert.Empty(await new AccessService(f.Db).Records(user).ToListAsync());
        Assert.Null(await new AccessService(f.Db).Current(f.Http(user)));
        user.Role = "User"; user.IsApproved = false; await f.Db.SaveChangesAsync();
        var result = await LanguageTaskFactory.BuildAsync(f.Db, JsonSerializer.Serialize(new[] { new { Language = "French", Targets = "123", AssignedUserId = user.Id } }), "admin");
        Assert.NotNull(result.Error);
    }
    [Fact]
    public async Task ExcelRolesCannotGrantPrivilegesOrChooseInitialPasswords()
    {
        await using var f = await Fixture.Create();
        using var book = new ClosedXML.Excel.XLWorkbook(); var sheet = book.AddWorksheet("Users");
        sheet.Cell(1,1).Value = "Username"; sheet.Cell(1,2).Value = "Role"; sheet.Cell(1,3).Value = "Password";
        sheet.Cell(2,1).Value = "imported"; sheet.Cell(2,2).Value = "Admin"; sheet.Cell(2,3).Value = "123456";
        using var stream = new MemoryStream(); book.SaveAs(stream); stream.Position = 0;
        var page = f.Page(new ImportUsersModel(f.Db) { ExcelFile = new FormFile(stream,0,stream.Length,"ExcelFile","users.xlsx") }, f.User("admin"));
        Assert.IsType<PageResult>(await page.OnPostAsync()); var user = await f.Db.PortalUsers.SingleAsync(x => x.Username == "imported");
        Assert.Equal("User",user.Role); Assert.False(user.IsApproved); Assert.False(user.IsActive); Assert.Null(user.PasswordHash);
    }
    [Fact]
    public async Task AmbiguousLegacyFullNamesDoNotExposeDeliverables()
    {
        await using var f = await Fixture.Create();
        var member = f.User("member"); var outsider = f.User("outsider"); outsider.FullName = member.FullName;
        var record = await f.Db.MDRecords.SingleAsync(x => x.RecordID == "MD-D"); record.AssignedTo = member.FullName; await f.Db.SaveChangesAsync();
        Assert.DoesNotContain(await new AccessService(f.Db).Records(member).ToListAsync(), x => x.RecordID == "MD-D");
        Assert.Empty(await new AccessService(f.Db).Records(outsider).ToListAsync());
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public QMSPortalDbContext Db { get; }

        public List<PortalUser> Users = new();
        private Fixture(SqliteConnection connection, QMSPortalDbContext db)
        {
            _connection = connection;
            Db = db;
        }

        public PortalUser User(string name) => Users.Single(x => x.Username == name);
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            connection.CreateCollation("ascii_general_ci", (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
            var db = new QMSPortalDbContext(new DbContextOptionsBuilder<QMSPortalDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var f = new Fixture(connection, db);
            foreach (var pair in new[]
            {
                ("admin", "Admin"),
                ("manager", "Manager"),
                ("lead", "Team Lead"),
                ("member", "User"),
                ("guest", "Guest"),
                ("outsider", "User"),
                ("other", "User")
            }

            )
            {
                var user = new PortalUser
                {
                    Username = pair.Item1,
                    FullName = pair.Item1 + " Person",
                    Role = pair.Item2
                };
                f.Users.Add(user);
                db.Add(user);
            }

            await db.SaveChangesAsync();
            var dept = new Department
            {
                Name = "Engineering",
                NormalizedName = "ENGINEERING"
            };
            var dept2 = new Department
            {
                Name = "Support",
                NormalizedName = "SUPPORT"
            };
            db.AddRange(dept, dept2);
            await db.SaveChangesAsync();
            f.User("manager").DepartmentId = dept.Id;
            var a = new Team
            {
                Name = "A",
                NormalizedName = "A",
                LeadUserId = f.User("lead").Id,
                DepartmentId = dept.Id,
                CreatedBy = "admin"
            };
            var b = new Team
            {
                Name = "B",
                NormalizedName = "B",
                DepartmentId = dept.Id,
                CreatedBy = "admin"
            };
            var c = new Team
            {
                Name = "C",
                NormalizedName = "C",
                DepartmentId = dept2.Id,
                CreatedBy = "admin"
            };
            db.AddRange(a, b, c);
            await db.SaveChangesAsync();
            db.Add(new TeamManager { TeamId = a.Id, UserId = f.User("manager").Id });
            db.Add(new TeamMembership { TeamId = a.Id, UserId = f.User("member").Id });
            db.Add(new TeamMembership { TeamId = c.Id, UserId = f.User("other").Id });
            var recA = new MDEntity
            {
                RecordID = "MD-A",
                TeamId = a.Id,
                MilestoneName = "Release",
                DeliverableName = "Release A",
                AssignedTo = "member",
                DeliverableType = "UGs",
                Status = "Completed"
            };
            var recB = new MDEntity
            {
                RecordID = "MD-B",
                TeamId = b.Id,
                MilestoneName = "Release",
                DeliverableName = "Release B",
                AssignedTo = "other",
                DeliverableType = "UGs",
                Status = "Completed"
            };
            db.AddRange(recA, recB, new MDEntity { RecordID = "MD-C", TeamId = c.Id, DeliverableName = "Release C", Status = "Not Started" }, new MDEntity { RecordID = "MD-D", DeliverableName = "Release D", Status = "Not Started" });
            await db.SaveChangesAsync();
            foreach (var rec in new[]
            {
                recA,
                recB
            }

            )
            {
                var built = await LanguageTaskFactory.BuildAsync(db, JsonSerializer.Serialize(new[] { new { Language = "German", Targets = "403024", AssignedUserId = f.User("member").Id } }), "admin");
                built.Tasks[0].DeliverableId = rec.Id;
                db.AddRange(built.Tasks);
            }

            await db.SaveChangesAsync();
            return f;
        }

        public HttpContext Http(PortalUser? actor)
        {
            var http = new DefaultHttpContext
            {
                Session = new MemorySession()
            };
            if (actor != null)
            {
                http.Session.SetString("Username", actor.Username);
                http.Session.SetString("SecurityStamp", actor.SecurityStamp.ToString());
                http.Session.SetString("LoggedInUser", actor.FullName);
                http.Session.SetString("UserRole", actor.Role);
            }

            return http;
        }

        public T Page<T>(T page, PortalUser? actor)
            where T : PageModel
        {
            var http = Http(actor);
            page.PageContext = new PageContext
            {
                HttpContext = http
            };
            page.TempData = new TempDataDictionary(http, new TempProvider());
            return page;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class MemorySession : ISession
    {
        private readonly Dictionary<string, byte[]> _data = new();
        public bool IsAvailable => true;
        public string Id => "test";
        public IEnumerable<string> Keys => _data.Keys;
        public void Clear() => _data.Clear();
        public void Remove(string key) => _data.Remove(key);
        public void Set(string key, byte[] value) => _data[key] = value;
        public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? value) => _data.TryGetValue(key, out value);
        public Task LoadAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken token = default) => Task.CompletedTask;
    }

    private sealed class TempProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext ctx) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext ctx, IDictionary<string, object> values)
        {
        }
    }
}
