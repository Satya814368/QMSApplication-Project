using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using QMSApplication.Pages;
using QMSApplication.Security;
using Xunit;
namespace QMSApplication.TaskTests;
public class CriticalNotificationTests
{
    [Fact] public async Task ManagerSeesOnlyManagedTeamsAcknowledgedAndPendingMembers()
    {
        await using var f=await Fixture.Create();var report=await f.Service.Build(f.Users["manager"],f.Notice.Id);
        Assert.False(report.Forbidden);Assert.Contains(report.Recipients,r=>r.Username=="a" && r.IsAcknowledged);
        Assert.Contains(report.Recipients,r=>r.Username=="a2" && !r.IsAcknowledged);
        Assert.DoesNotContain(report.Recipients,r=>r.Username=="b" || r.Username=="unassigned");
        Assert.Equal(report.Recipients.Count,report.Acknowledged+report.Pending);
    }
    [Fact] public async Task LeadSeesOnlyLedTeamAndCannotForgeAnotherTeamOrDepartment()
    {
        await using var f=await Fixture.Create();var actor=f.Users["lead"];
        var report=await f.Service.Build(actor,f.Notice.Id);Assert.Contains(report.Recipients,r=>r.Username=="a");Assert.DoesNotContain(report.Recipients,r=>r.Username=="b");
        Assert.True((await f.Service.Build(actor,f.Notice.Id,null,f.OtherTeam.Id)).Forbidden);
        Assert.True((await f.Service.Build(actor,f.Notice.Id,f.OtherDepartment.Id)).Forbidden);
    }
    [Fact] public async Task AdminCanSelectAnyDepartmentAndSeeCurrentAcknowledgements()
    {
        await using var f=await Fixture.Create();var admin=f.Users["admin"];
        var all=await f.Service.Build(admin,f.Notice.Id);Assert.Equal(2,all.Departments.Count);Assert.Contains(all.Recipients,r=>r.Username=="a");Assert.Contains(all.Recipients,r=>r.Username=="b");
        var filtered=await f.Service.Build(admin,f.Notice.Id,f.OtherDepartment.Id);
        Assert.Single(filtered.Recipients);Assert.Equal("b",filtered.Recipients[0].Username);Assert.True(filtered.Recipients[0].IsAcknowledged);Assert.NotNull(filtered.Recipients[0].AcknowledgedAtUtc);
        var unassigned=await f.Service.Build(admin,f.Notice.Id,0);Assert.Contains(unassigned.Recipients,r=>r.Username=="admin");
    }
    [Fact] public async Task RoleTargetingDoesNotCountPeopleWhoDidNotReceiveTheNotice()
    {
        await using var f=await Fixture.Create();f.Notice.TargetRole="User";await f.Db.SaveChangesAsync();
        var report=await f.Service.Build(f.Users["manager"],f.Notice.Id);Assert.Equal(new[]{"a","a2"},report.Recipients.Select(r=>r.Username).OrderBy(x=>x));
        f.Notice.TargetRole="TL";await f.Db.SaveChangesAsync();report=await f.Service.Build(f.Users["manager"],f.Notice.Id);
        Assert.Equal("lead",Assert.Single(report.Recipients).Username);
    }
    [Fact] public async Task InactivePendingAndUnknownAccountsAreExcluded()
    {
        await using var f=await Fixture.Create();f.Users["a"].IsActive=false;f.Users["a2"].IsApproved=false;await f.Db.SaveChangesAsync();
        var report=await f.Service.Build(f.Users["manager"],f.Notice.Id);Assert.DoesNotContain(report.Recipients,r=>r.Username=="a" || r.Username=="a2");
    }
    [Fact] public async Task UsersGuestsAndDisabledManagersCannotTrack()
    {
        await using var f=await Fixture.Create();foreach(var name in new[]{"a","guest"})Assert.True((await f.Service.Build(f.Users[name])).Forbidden);
        f.Users["manager"].IsActive=false;Assert.True((await f.Service.Build(f.Users["manager"])).Forbidden);
    }
    [Fact] public async Task DraftsAndForeignNotificationIdsAreNotExposedInTeamReports()
    {
        await using var f=await Fixture.Create();var draft=new Notification{Title="Draft secret",Message="Draft",IsPublished=false};
        var adminOnly=new Notification{Title="Admin secret",Message="Admin",TargetRole="Admin",IsPublished=true};f.Db.Notifications.AddRange(draft,adminOnly);await f.Db.SaveChangesAsync();
        var report=await f.Service.Build(f.Users["lead"]);Assert.DoesNotContain(report.Notifications,n=>n.Id==draft.Id || n.Id==adminOnly.Id);
        Assert.True((await f.Service.Build(f.Users["lead"],adminOnly.Id)).Forbidden);
    }
    [Fact] public async Task DuplicateHistoricalStatusRowsDoNotDoubleCountOrHideAcknowledgement()
    {
        await using var f=await Fixture.Create();f.Db.NotificationUserStatuses.Add(new NotificationUserStatus{NotificationId=f.Notice.Id,Username="a",IsAcknowledged=false});await f.Db.SaveChangesAsync();
        var report=await f.Service.Build(f.Users["manager"],f.Notice.Id);Assert.Single(report.Recipients.Where(r=>r.Username=="a"));Assert.True(report.Recipients.Single(r=>r.Username=="a").IsAcknowledged);
    }
    [Fact] public async Task NewAndEditedNotificationsNeedNoPriorityOrMandatoryColumns()
    {
        await using var f=await Fixture.Create();var create=new NotificationCreateModel(f.Db){PageContext=f.Context("admin"),Title="New",Message="Text",IsPublished=true,TargetRole="Team Lead"};
        Assert.IsType<RedirectToPageResult>(await create.OnPostAsync());var notice=await f.Db.Notifications.SingleAsync(n=>n.Title=="New");Assert.NotNull(notice);Assert.Null(f.Db.Model.FindEntityType(typeof(Notification))!.FindProperty("Priority"));Assert.Null(f.Db.Model.FindEntityType(typeof(Notification))!.FindProperty("IsMandatory"));
        var edit=new NotificationEditModel(f.Db){PageContext=f.Context("manager"),Id=notice.Id,Title="Edited",Message="Text",TargetRole="User",IsPublished=true};
        Assert.IsType<RedirectToPageResult>(await edit.OnPostAsync());Assert.NotNull(notice);Assert.Null(f.Db.Model.FindEntityType(typeof(Notification))!.FindProperty("Priority"));Assert.Null(f.Db.Model.FindEntityType(typeof(Notification))!.FindProperty("IsMandatory"));
    }
    [Fact] public async Task InvalidTargetRolesCannotBeSaved()
    {
        await using var f=await Fixture.Create();var create=new NotificationCreateModel(f.Db){PageContext=f.Context("admin"),Title="Invalid",Message="Text",TargetRole="Bad role"};
        Assert.IsType<PageResult>(await create.OnPostAsync());Assert.False(create.ModelState.IsValid);Assert.False(await f.Db.Notifications.AnyAsync(n=>n.Title=="Invalid"));
    }
    [Fact] public async Task TeamLeadTrackingDoesNotGrantPublishingOrEditing()
    {
        await using var f=await Fixture.Create();var create=new NotificationCreateModel(f.Db){PageContext=f.Context("lead"),Title="Blocked",Message="Text"};
        Assert.Equal("/Notifications",Assert.IsType<RedirectToPageResult>(await create.OnPostAsync()).PageName);
        var page=new NotificationAdminModel(f.Db){PageContext=f.Context("lead"),MetadataProvider=new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider()};Assert.IsType<PageResult>(await page.OnGetAsync());Assert.False(page.CanManage);
        Assert.IsType<PartialViewResult>(await page.OnGetAcknowledgementsAsync(f.Notice.Id));
        Assert.Equal("/AccessDenied",Assert.IsType<RedirectToPageResult>(await page.OnPostDeleteAsync(f.Notice.Id)).PageName);
        var denied=new NotificationAdminModel(f.Db){PageContext=f.Context("a")};Assert.Equal("/AccessDenied",Assert.IsType<RedirectToPageResult>(await denied.OnGetAsync()).PageName);
        Assert.Equal(403,Assert.IsType<StatusCodeResult>(await denied.OnGetAcknowledgementsAsync(f.Notice.Id)).StatusCode);
    }
    [Fact] public async Task EveryNoticeCanBeAcknowledgedAndReportUpdatesForCorrectUser()
    {
        await using var f=await Fixture.Create();
        var page=new NotificationsModel(f.Db){PageContext=f.Context("a2")};Assert.IsType<RedirectToPageResult>(await page.OnPostAcknowledgeAsync(f.Notice.Id));
        var report=await f.Service.Build(f.Users["manager"],f.Notice.Id);Assert.True(report.Recipients.Single(r=>r.Username=="a2").IsAcknowledged);
    }
    [Fact] public async Task TeamLeadAliasesWorkInRecipientInboxAndReport()
    {
        await using var f=await Fixture.Create();f.Notice.TargetRole="Team Lead";await f.Db.SaveChangesAsync();
        Assert.Single(await NotificationReportService.Relevant(f.Db,"TL").ToListAsync());
        f.Notice.TargetRole="Unknown";await f.Db.SaveChangesAsync();Assert.Empty(await NotificationReportService.Relevant(f.Db,"Guest").ToListAsync());
    }
    [Fact] public async Task RemovingObsoleteColumnsPreservesNotificationAndAcknowledgementData()
    {
        await using var f=await Fixture.Create();
        await f.Db.Database.ExecuteSqlRawAsync("ALTER TABLE Notifications ADD COLUMN Priority varchar(20) NOT NULL DEFAULT 'Normal'");
        await f.Db.Database.ExecuteSqlRawAsync("ALTER TABLE Notifications ADD COLUMN IsMandatory int NOT NULL DEFAULT 0");
        var before=await f.Db.NotificationUserStatuses.AsNoTracking().ToListAsync();
        await f.Db.Database.ExecuteSqlRawAsync("ALTER TABLE Notifications DROP COLUMN IsMandatory");
        await f.Db.Database.ExecuteSqlRawAsync("ALTER TABLE Notifications DROP COLUMN Priority");
        Assert.Equal("Notice",(await f.Db.Notifications.AsNoTracking().SingleAsync()).Title);
        Assert.Equal(before.Count,await f.Db.NotificationUserStatuses.CountAsync());
        var report=await f.Service.Build(f.Users["manager"],f.Notice.Id);
        Assert.True(report.Recipients.Single(r=>r.Username=="a").IsAcknowledged);
        Assert.False(report.Recipients.Single(r=>r.Username=="a2").IsAcknowledged);
    }
    private class Fixture:IAsyncDisposable
    {
        private readonly SqliteConnection _connection;public QMSPortalDbContext Db{get;}public NotificationReportService Service=>new(Db);
        public Dictionary<string,PortalUser> Users{get;}=new();public Notification Notice{get;private set;}=null!;public Team OtherTeam{get;private set;}=null!;public Department OtherDepartment{get;private set;}=null!;
        private Fixture(SqliteConnection c,QMSPortalDbContext db){_connection=c;Db=db;}
        public static async Task<Fixture> Create()
        {
            var c=new SqliteConnection("Data Source=:memory:");await c.OpenAsync();c.CreateCollation("ascii_general_ci",(a,b)=>string.Compare(a,b,StringComparison.OrdinalIgnoreCase));var db=new QMSPortalDbContext(new DbContextOptionsBuilder<QMSPortalDbContext>().UseSqlite(c).Options);await db.Database.EnsureCreatedAsync();var f=new Fixture(c,db);
            var aDept=new Department{Name="Labelling",NormalizedName="LABELLING"};f.OtherDepartment=new Department{Name="Software",NormalizedName="SOFTWARE"};db.AddRange(aDept,f.OtherDepartment);await db.SaveChangesAsync();
            foreach(var (name,role) in new[]{("admin","Admin"),("manager","Manager"),("lead","Team Lead"),("a","User"),("a2","User"),("b","User"),("unassigned","User"),("guest","Guest")})
            {var user=new PortalUser{Username=name,FullName=name,Role=role,IsActive=true,IsApproved=true,DepartmentId=name=="admin"?null:name=="b"?f.OtherDepartment.Id:aDept.Id};f.Users[name]=user;db.Add(user);}await db.SaveChangesAsync();
            var team=new Team{Name="Team A",NormalizedName="TEAM A",DepartmentId=aDept.Id,LeadUserId=f.Users["lead"].Id};f.OtherTeam=new Team{Name="Team B",NormalizedName="TEAM B",DepartmentId=f.OtherDepartment.Id};db.AddRange(team,f.OtherTeam);await db.SaveChangesAsync();
            db.TeamManagers.Add(new TeamManager{TeamId=team.Id,UserId=f.Users["manager"].Id});foreach(var name in new[]{"a","a2"})db.TeamMemberships.Add(new TeamMembership{TeamId=team.Id,UserId=f.Users[name].Id});db.TeamMemberships.Add(new TeamMembership{TeamId=f.OtherTeam.Id,UserId=f.Users["b"].Id});
            f.Notice=new Notification{Title="Notice",Message="Text",IsPublished=true};db.Add(f.Notice);await db.SaveChangesAsync();
            foreach(var name in new[]{"a","b"})db.NotificationUserStatuses.Add(new NotificationUserStatus{NotificationId=f.Notice.Id,Username=name,IsRead=true,IsAcknowledged=true,AcknowledgedAtUtc=DateTime.UtcNow});await db.SaveChangesAsync();return f;
        }
        public PageContext Context(string name){var http=new DefaultHttpContext{Session=new MemorySession()};http.Session.SetString("Username",name);http.Session.SetString("UserRole",Users[name].Role);http.Items[typeof(AccessService)]=Users[name];return new PageContext{HttpContext=http,ViewData=new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(),new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary())};}
        public async ValueTask DisposeAsync(){await Db.DisposeAsync();await _connection.DisposeAsync();}
    }
    private class MemorySession:ISession
    {
        private readonly Dictionary<string,byte[]> _data=new();public bool IsAvailable=>true;public string Id=>"test";public IEnumerable<string> Keys=>_data.Keys;
        public void Clear()=>_data.Clear();public void Remove(string key)=>_data.Remove(key);public void Set(string key,byte[] value)=>_data[key]=value;
        public bool TryGetValue(string key,[System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? value)=>_data.TryGetValue(key,out value);
        public Task LoadAsync(CancellationToken token=default)=>Task.CompletedTask;public Task CommitAsync(CancellationToken token=default)=>Task.CompletedTask;
    }
}
