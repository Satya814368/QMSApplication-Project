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
public class GuidelineTests
{
    [Fact] public async Task OnlyAdminAndManagerCanAddEditOrDelete()
    {
        await using var f = await Fixture.Create();
        foreach(var name in new[] { "user", "user2", "lead", "guest", "inactive", "pending" })
        {
            var actor = f.Users[name]; Assert.NotNull(await f.Service.Save(actor,null,Guid.Empty,"Title","Text",0,8));
            Assert.NotNull(await f.Service.Delete(actor,1,Guid.Empty));
        }
        Assert.Empty(await f.Db.Guidelines.ToListAsync());
        Assert.Null(await f.Service.Save(f.Users["manager"],null,Guid.Empty,"Manager guideline","Text",0,8));
        Assert.Null(await f.Service.Save(f.Users["admin"],null,Guid.Empty,"Admin guideline","Text",0,8));
        Assert.Equal(2,await f.Db.Guidelines.CountAsync());
    }
    [Fact] public async Task OnlyTargetedRolesSeeGuidanceAndGetAnUnreadDot()
    {
        await using var f = await Fixture.Create(); await f.Add(8);
        Assert.True(await f.Service.HasUnread(f.Users["user"]));
        Assert.False(await f.Service.HasUnread(f.Users["lead"]));
        Assert.Empty(await f.Service.Open(f.Users["lead"]));
        Assert.Empty(await f.Service.Open(f.Users["guest"]));
        Assert.Single(await f.Service.Open(f.Users["user"]));
        Assert.False(await f.Service.HasUnread(f.Users["user"]));
    }
    [Fact] public async Task OpeningClearsOnlyThatUsersDotAndAnEditMakesItUnreadAgain()
    {
        await using var f = await Fixture.Create(); var g = await f.Add(8);
        await f.Service.Open(f.Users["user"]);
        Assert.False(await f.Service.HasUnread(f.Users["user"])); Assert.True(await f.Service.HasUnread(f.Users["user2"]));
        var previous = g.Version;
        Assert.Null(await f.Service.Save(f.Users["manager"],g.Id,g.Version,"Updated","Updated text",0,8));
        Assert.NotEqual(previous,g.Version); Assert.True(await f.Service.HasUnread(f.Users["user"]));
        var list = await f.Service.Open(f.Users["user"]); Assert.Equal("Updated text",Assert.Single(list).Content);
        Assert.False(await f.Service.HasUnread(f.Users["user"])); Assert.True(await f.Service.HasUnread(f.Users["user2"]));
    }
    [Fact] public async Task PollingDoesNotMarkAnythingRead()
    {
        await using var f = await Fixture.Create(); await f.Add(8);
        var page = f.Page("user"); var response = Assert.IsType<JsonResult>(await page.OnGetUnreadAsync());
        Assert.Contains("True",response.Value!.ToString()); Assert.Empty(await f.Db.GuidelineReadStatuses.ToListAsync());
        await page.OnGetAsync(); Assert.False(await f.Service.HasUnread(f.Users["user"]));
    }
    [Fact] public async Task NewAudienceGetsTheUpdateAndRemovedAudienceLosesAccess()
    {
        await using var f = await Fixture.Create(); var g = await f.Add(8); await f.Service.Open(f.Users["user"]);
        Assert.Null(await f.Service.Save(f.Users["admin"],g.Id,g.Version,"For leads","Lead text",0,4));
        Assert.True(await f.Service.HasUnread(f.Users["lead"])); Assert.False(await f.Service.HasUnread(f.Users["user"]));
        Assert.Empty(await f.Service.Open(f.Users["user"]));
        Assert.Single(await f.Service.Open(f.Users["lead"]));
    }
    [Fact] public async Task DeleteRemovesGuidelineAndItsUnreadDotWithoutErasingAudit()
    {
        await using var f = await Fixture.Create(); var g = await f.Add(8);
        Assert.Null(await f.Service.Delete(f.Users["manager"],g.Id,g.Version));
        Assert.False(await f.Service.HasUnread(f.Users["user"])); Assert.Empty(await f.Service.Open(f.Users["user"]));
        Assert.False((await f.Db.Guidelines.SingleAsync()).IsActive);
        Assert.Contains(await f.Db.TeamAuditEvents.ToListAsync(),e=>e.Action=="Guideline deleted");
    }
    [Fact] public async Task StaleEditOrDeleteCannotOverwriteANewerRevision()
    {
        await using var f = await Fixture.Create(); var g = await f.Add(8); var old = g.Version;
        Assert.Null(await f.Service.Save(f.Users["admin"],g.Id,old,"New","New",0,8));
        Assert.NotNull(await f.Service.Save(f.Users["manager"],g.Id,old,"Stale","Stale",0,8));
        Assert.NotNull(await f.Service.Delete(f.Users["manager"],g.Id,old)); Assert.Equal("New",g.Title);
    }
    [Fact] public async Task MultipleGuidelinesAndMultipleRolesAreReadTogether()
    {
        await using var f = await Fixture.Create(); await f.Add(8|4|16); await f.Add(8);
        Assert.Equal(2,(await f.Service.Open(f.Users["user"])).Count);
        Assert.False(await f.Service.HasUnread(f.Users["user"])); Assert.True(await f.Service.HasUnread(f.Users["guest"]));
        Assert.Single(await f.Service.Open(f.Users["guest"])); Assert.False(await f.Service.HasUnread(f.Users["guest"]));
    }
    [Fact] public async Task ManagersCanManageUntargetedGuidanceWithoutReadingOnBehalfOfMembers()
    {
        await using var f = await Fixture.Create(); await f.Add(8);
        Assert.Single(await f.Service.Open(f.Users["manager"]));
        Assert.Empty(await f.Db.GuidelineReadStatuses.ToListAsync()); Assert.True(await f.Service.HasUnread(f.Users["user"]));
    }
    [Fact] public async Task InvalidOrEmptyAudienceAndBlankTextNeverSave()
    {
        await using var f = await Fixture.Create();
        foreach(var mask in new[] { 0,32,-1 }) Assert.NotNull(await f.Service.Save(f.Users["admin"],null,Guid.Empty,"Title","Text",0,mask));
        Assert.NotNull(await f.Service.Save(f.Users["admin"],null,Guid.Empty," ","Text",0,8));
        Assert.NotNull(await f.Service.Save(f.Users["admin"],null,Guid.Empty,"Title"," ",0,8)); Assert.Empty(await f.Db.Guidelines.ToListAsync());
    }
    [Fact] public async Task ReaderCannotForgeManagementHandlersOrUseEditLinksToClearDot()
    {
        await using var f = await Fixture.Create(); var g = await f.Add(8); var page = f.Page("user");
        Assert.Equal("/AccessDenied",Assert.IsType<RedirectToPageResult>(await page.OnGetAsync(g.Id)).PageName);
        Assert.True(await f.Service.HasUnread(f.Users["user"]));
        Assert.Equal("/AccessDenied",Assert.IsType<RedirectToPageResult>(await page.OnPostSaveAsync()).PageName);
        Assert.Equal("/AccessDenied",Assert.IsType<RedirectToPageResult>(await page.OnPostDeleteAsync(g.Id,g.Version)).PageName);
        Assert.True(g.IsActive);
    }
    [Fact] public async Task InactivePendingAndUnknownRolesNeverGetGuidanceOrBadge()
    {
        await using var f = await Fixture.Create(); await f.Add(31);
        foreach(var name in new[] { "inactive", "pending", "unknown" }) { Assert.False(await f.Service.HasUnread(f.Users[name])); Assert.Empty(await f.Service.Open(f.Users[name])); }
    }
    [Fact] public void LegacyHtmlIsConvertedToTextAndNewHtmlRemainsPlainText()
    {
        var g = new Guideline { Content="<h2>Heading</h2><p>Use &amp; review</p>" };
        Assert.Contains("Heading",GuidelineService.DisplayContent(g)); Assert.Contains("Use & review",GuidelineService.DisplayContent(g));
        Assert.DoesNotContain("<h2>",GuidelineService.DisplayContent(g));
        g.ContentIsPlainText=true; g.Content="<script>alert(1)</script>"; Assert.Equal(g.Content,GuidelineService.DisplayContent(g));
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public QMSPortalDbContext Db { get; }
        public GuidelineService Service => new(Db);
        public Dictionary<string,PortalUser> Users { get; } = new();
        private Fixture(SqliteConnection c,QMSPortalDbContext db) { _connection=c; Db=db; }
        public static async Task<Fixture> Create()
        {
            var c=new SqliteConnection("Data Source=:memory:"); await c.OpenAsync();
            c.CreateCollation("ascii_general_ci",(a,b)=>string.Compare(a,b,StringComparison.OrdinalIgnoreCase));
            var db=new QMSPortalDbContext(new DbContextOptionsBuilder<QMSPortalDbContext>().UseSqlite(c).Options);await db.Database.EnsureCreatedAsync();var f=new Fixture(c,db);
            foreach(var (name,role) in new[] { ("admin","Admin"),("manager","Manager"),("lead","Team Lead"),("user","User"),("user2","User"),("guest","Guest"),("inactive","User"),("pending","User"),("unknown","Unknown") })
            {
                var u=new PortalUser {Username=name,FullName=name,Role=role,IsActive=name!="inactive",IsApproved=name!="pending"}; f.Users[name]=u;db.PortalUsers.Add(u);
            }
            await db.SaveChangesAsync();return f;
        }
        public async Task<Guideline> Add(int roles) { Assert.Null(await Service.Save(Users["admin"],null,Guid.Empty,"Guideline","Text",0,roles));return await Db.Guidelines.OrderByDescending(x=>x.Id).FirstAsync(); }
        public GuidelinesModel Page(string name) { var context=new DefaultHttpContext();context.Items[typeof(AccessService)]=Users[name];return new GuidelinesModel(Db) {PageContext=new PageContext {HttpContext=context}}; }
        public async ValueTask DisposeAsync() {await Db.DisposeAsync();await _connection.DisposeAsync();}
    }
}
