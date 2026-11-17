using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using QMSApplication.Data;
using QMSApplication.Models;
using QMSApplication.Pages;
using Xunit;

namespace QMSApplication.TaskTests;

public class TeamFoundationTests
{
    [Fact]
    public async Task AdminAndManagerCreateTeamsAndDuplicateNamesAreRejected()
    {
        await using var f=await Fixture.Create();
        Assert.NotNull(await f.Service.Create("worker","New"));
        Assert.Null(await f.Service.Create("manager","New"));
        Assert.Null(await f.Service.Create("admin","Admin team"));
        Assert.NotNull(await f.Service.Create("admin"," new "));
        Assert.Equal(4,await f.Db.Teams.CountAsync());
    }
    [Fact]
    public async Task ManagerSeesOnlyAssignedTeams()
    {
        await using var f=await Fixture.Create();
        Assert.Equal(new[]{f.A.Id},await f.Service.ManagedTeams(f.Manager).Select(x=>x.Id).ToArrayAsync());
        Assert.Empty(await f.Service.ManagedTeams(f.Worker).ToListAsync());
        Assert.Equal(2,await f.Service.ManagedTeams(f.Admin).CountAsync());
    }
    [Fact]
    public async Task LeadCanAddUnassignedAndTransferFromOtherTeamWithAudit()
    {
        await using var f=await Fixture.Create();
        Assert.Null(await f.Service.MoveMember("lead",f.Worker.Id,f.A.Id,null,null));
        Assert.Equal(f.A.Id,(await f.Db.TeamMemberships.SingleAsync()).TeamId);
        Assert.Null(await f.Service.MoveMember("admin",f.Worker.Id,f.B.Id,f.A.Id,"Project allocation"));
        Assert.Null(await f.Service.MoveMember("lead",f.Worker.Id,f.A.Id,f.B.Id,"New project"));
        Assert.Equal(f.A.Id,(await f.Db.TeamMemberships.SingleAsync()).TeamId);
        Assert.Contains("New project",(await f.Db.TeamAuditEvents.OrderBy(x=>x.Id).LastAsync()).DetailsJson);
    }
    [Fact]
    public async Task LeadCannotMoveIntoSomeoneElsesTeamOrAssignLeadershipAccounts()
    {
        await using var f=await Fixture.Create();
        Assert.NotNull(await f.Service.MoveMember("lead",f.Worker.Id,f.B.Id,null,null));
        Assert.NotNull(await f.Service.MoveMember("lead",f.Manager.Id,f.A.Id,null,null));
        Assert.Empty(await f.Db.TeamMemberships.ToListAsync());
    }
    [Fact]
    public async Task StaleTransferAndMissingTransferReasonLeaveMembershipUnchanged()
    {
        await using var f=await Fixture.Create();
        Assert.Null(await f.Service.MoveMember("admin",f.Worker.Id,f.B.Id,null,null));
        Assert.NotNull(await f.Service.MoveMember("lead",f.Worker.Id,f.A.Id,null,"Old page"));
        Assert.NotNull(await f.Service.MoveMember("lead",f.Worker.Id,f.A.Id,f.B.Id,null));
        Assert.Equal(f.B.Id,(await f.Db.TeamMemberships.SingleAsync()).TeamId);
        Assert.Equal(1,await f.Db.TeamAuditEvents.CountAsync());
    }
    [Fact]
    public async Task OnlyManagedMembershipCanBeRemoved()
    {
        await using var f=await Fixture.Create();
        Assert.Null(await f.Service.MoveMember("admin",f.Worker.Id,f.B.Id,null,null));
        Assert.NotNull(await f.Service.RemoveMember("manager",f.Worker.Id,f.B.Id,"No"));
        Assert.Null(await f.Service.RemoveMember("admin",f.Worker.Id,f.B.Id,"Team change"));
        Assert.Empty(await f.Db.TeamMemberships.ToListAsync());
    }
    [Fact]
    public async Task OnlyAdminMapsOwnershipAndExistingAssignmentsAreRetained()
    {
        await using var f=await Fixture.Create();
        var version=f.R1.ConcurrencyToken;
        Assert.Null(f.R1.TeamId);
        Assert.NotNull(await f.Service.MapDeliverable("manager",f.R1.Id,f.A.Id,version));
        Assert.Null(await f.Service.MapDeliverable("admin",f.R1.Id,f.A.Id,version));
        var record=await f.Db.MDRecords.SingleAsync(x=>x.Id==f.R1.Id);
        Assert.Equal(f.A.Id,record.TeamId);Assert.Equal("worker",record.AssignedTo);
        Assert.NotNull(await f.Service.MapDeliverable("admin",f.R1.Id,f.B.Id,version));
        Assert.Equal(f.A.Id,(await f.Db.MDRecords.SingleAsync(x=>x.Id==f.R1.Id)).TeamId);
    }
    [Fact]
    public async Task ManagerCanDeleteOwnedDeliverableAndCannotDeleteOtherTeam()
    {
        await using var f=await Fixture.Create();await f.MapRecords();
        var page=f.Page(f.Manager);Assert.IsType<RedirectToPageResult>(await page.OnPostDeleteAsync("MD-1"));
        Assert.False(await f.Db.MDRecords.AnyAsync(x=>x.RecordID=="MD-1"));
        page=f.Page(f.Manager);await page.OnPostDeleteAsync("MD-2");
        Assert.True(await f.Db.MDRecords.AnyAsync(x=>x.RecordID=="MD-2"));
        Assert.Contains("teams assigned",page.TempData["ErrorMessage"]!.ToString());
    }
    [Fact]
    public async Task MixedScopeBulkDeleteIsAtomicAndUnassignedRecordsAreProtected()
    {
        await using var f=await Fixture.Create();await f.MapRecords();
        var page=f.Page(f.Manager);page.SelectedRecordIds=new(){"MD-1","MD-2"};await page.OnPostBulkDeleteAsync();
        Assert.Equal(3,await f.Db.MDRecords.CountAsync());Assert.Empty(await f.Db.MDAuditLogs.ToListAsync());
        page=f.Page(f.Manager);await page.OnPostDeleteAsync("MD-3");Assert.Equal(3,await f.Db.MDRecords.CountAsync());
    }
    [Fact]
    public async Task ManagerBulkDeleteWithinOwnedTeamSucceedsAndRevocationAppliesImmediately()
    {
        await using var f=await Fixture.Create();
        Assert.Null(await f.Service.MapDeliverable("admin",f.R1.Id,f.A.Id,f.R1.ConcurrencyToken));
        var link=await f.Db.TeamManagers.SingleAsync();f.Db.TeamManagers.Remove(link);await f.Db.SaveChangesAsync();
        var denied=f.Page(f.Manager);await denied.OnPostDeleteAsync("MD-1");Assert.Equal(3,await f.Db.MDRecords.CountAsync());
        f.Db.TeamManagers.Add(new TeamManager{TeamId=f.A.Id,UserId=f.Manager.Id});await f.Db.SaveChangesAsync();
        var page=f.Page(f.Manager);page.SelectedRecordIds=new(){"MD-1"};await page.OnPostBulkDeleteAsync();
        Assert.Equal(2,await f.Db.MDRecords.CountAsync());Assert.Single(await f.Db.MDAuditLogs.ToListAsync());
    }
    [Theory]
    [InlineData("User")][InlineData("Team Lead")][InlineData("Guest")]
    public async Task RestrictedRolesGetFriendlyDeleteDenial(string role)
    {
        await using var f=await Fixture.Create();f.Worker.Role=role;await f.Db.SaveChangesAsync();
        var page=f.Page(f.Worker);Assert.IsType<RedirectToPageResult>(await page.OnPostDeleteAsync("MD-1"));
        Assert.NotNull(page.TempData["ErrorMessage"]);Assert.Equal(3,await f.Db.MDRecords.CountAsync());
    }
    [Fact]
    public async Task AdminDeletesUnassignedAndManagerSeesOnlyOwnedRecords()
    {
        await using var f=await Fixture.Create();await f.MapRecords();
        var page=f.Page(f.Manager);page.OnGet();
        Assert.True(page.Records.Single(x=>x.RecordID=="MD-1").CanDelete);
        Assert.DoesNotContain(page.Records, x=>x.RecordID=="MD-2");
        Assert.DoesNotContain(page.Records, x=>x.RecordID=="MD-3");
        await f.Page(f.Admin).OnPostDeleteAsync("MD-3");Assert.Equal(2,await f.Db.MDRecords.CountAsync());
    }
    [Fact]
    public async Task ManagerAndLeadAssignmentsRequireAdminAndExpectedRoles()
    {
        await using var f=await Fixture.Create();
        Assert.NotNull(await f.Service.SetLead("manager",f.B.Id,f.Lead.Id,f.B.Version));
        Assert.NotNull(await f.Service.SetLead("admin",f.B.Id,f.Worker.Id,f.B.Version));
        Assert.NotNull(await f.Service.SetLead("admin",f.B.Id,f.Lead.Id,f.B.Version));
        Assert.NotNull(await f.Service.SetManager("lead",f.B.Id,f.Manager.Id,false,f.B.Version));
        Assert.Null(await f.Service.SetManager("admin",f.B.Id,f.Manager.Id,false,f.B.Version));
        Assert.Equal(2,await f.Service.ManagedTeams(f.Manager).CountAsync());
    }
    [Fact]
    public void MySqlSnapshotMatchesAndMigrationDoesNotAssignOrDeleteExistingData()
    {
        using var db=new QMSPortalDbContext(new DbContextOptionsBuilder<QMSPortalDbContext>().UseMySql("Server=localhost;Database=Validation;User=test;Password=test;",new MySqlServerVersion(new Version(8,4,0))).Options);
        var snapshot=db.GetService<IMigrationsAssembly>().ModelSnapshot!;
        var model=db.GetService<IModelRuntimeInitializer>().Initialize(snapshot.Model,designTime:true);
        Assert.Empty(db.GetService<IMigrationsModelDiffer>().GetDifferences(model.GetRelationalModel(),db.GetService<IDesignTimeModel>().Model.GetRelationalModel()));
        var sql=db.GetService<IMigrator>().GenerateScript("20261022120000_AddLanguageTasks","20261029120000_AddTeamFoundation");
        Assert.Contains("ADD `TeamId` bigint NULL",sql);Assert.Contains("CREATE TABLE `TeamMemberships`",sql);
        Assert.DoesNotContain("DELETE FROM",sql);Assert.DoesNotContain("UPDATE `MDRecords`",sql);
    }
    private sealed class Fixture:IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public QMSPortalDbContext Db {get;}
        public TeamSetupService Service {get;}
        public PortalUser Admin {get;}=new(){Username="admin",FullName="Admin",Role="Admin"};
        public PortalUser Manager {get;}=new(){Username="manager",FullName="Manager",Role="Manager"};
        public PortalUser Lead {get;}=new(){Username="lead",FullName="Lead",Role="Team Lead"};
        public PortalUser Worker {get;}=new(){Username="worker",FullName="Worker",Role="User"};
        public Team A {get;}=new(){Name="A",NormalizedName="A",CreatedBy="admin"};
        public Team B {get;}=new(){Name="B",NormalizedName="B",CreatedBy="admin"};
        public MDEntity R1 {get;}=new(){RecordID="MD-1",AssignedTo="worker"};
        public MDEntity R2 {get;}=new(){RecordID="MD-2"};
        private Fixture(SqliteConnection connection){_connection=connection;Db=new(new DbContextOptionsBuilder<QMSPortalDbContext>().UseSqlite(connection).Options);Service=new(Db);}
        public static async Task<Fixture> Create()
        {
            var c=new SqliteConnection("Data Source=:memory:;Foreign Keys=True");await c.OpenAsync();var f=new Fixture(c);await f.Db.Database.EnsureCreatedAsync();
            f.Db.PortalUsers.AddRange(f.Admin,f.Manager,f.Lead,f.Worker);await f.Db.SaveChangesAsync();f.A.LeadUserId=f.Lead.Id;
            f.Db.Teams.AddRange(f.A,f.B);f.Db.MDRecords.AddRange(f.R1,f.R2,new MDEntity{RecordID="MD-3"});await f.Db.SaveChangesAsync();
            f.Db.TeamManagers.Add(new TeamManager{TeamId=f.A.Id,UserId=f.Manager.Id});await f.Db.SaveChangesAsync();return f;
        }
        public async Task MapRecords(){Assert.Null(await Service.MapDeliverable("admin",R1.Id,A.Id,R1.ConcurrencyToken));Assert.Null(await Service.MapDeliverable("admin",R2.Id,B.Id,R2.ConcurrencyToken));}
        public MDModel Page(PortalUser actor){var http=new DefaultHttpContext{Session=new MemorySession()};http.Session.SetString("Username",actor.Username);http.Session.SetString("SecurityStamp",actor.SecurityStamp.ToString());http.Session.SetString("LoggedInUser",actor.FullName);return new MDModel(Db){PageContext=new PageContext{HttpContext=http},TempData=new TempDataDictionary(http,new MemoryTemp())};}
        public async ValueTask DisposeAsync(){await Db.DisposeAsync();await _connection.DisposeAsync();}
    }
    private sealed class MemorySession:ISession
    {
        private readonly Dictionary<string,byte[]> _values=new();public bool IsAvailable=>true;public string Id=>"test";public IEnumerable<string> Keys=>_values.Keys;
        public void Clear()=>_values.Clear();public void Remove(string key)=>_values.Remove(key);public void Set(string key,byte[] value)=>_values[key]=value;
        public bool TryGetValue(string key,[System.Diagnostics.CodeAnalysis.NotNullWhen(true)]out byte[]? value)=>_values.TryGetValue(key,out value);
        public Task LoadAsync(CancellationToken cancellationToken=default)=>Task.CompletedTask;public Task CommitAsync(CancellationToken cancellationToken=default)=>Task.CompletedTask;
    }
    private sealed class MemoryTemp:ITempDataProvider{public IDictionary<string,object> LoadTempData(HttpContext context)=>new Dictionary<string,object>();public void SaveTempData(HttpContext context,IDictionary<string,object> values){}}
}
