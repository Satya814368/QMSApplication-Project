using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using QMSApplication.Security;
using Xunit;
namespace QMSApplication.TaskTests;
public class ProfileServiceTests
{
    [Fact] public async Task UnassignedAdminGetsNoPersonalTeamsAndDepartmentScopeRemainsExplicit()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();connection.CreateCollation("ascii_general_ci",(a,b)=>string.Compare(a,b,StringComparison.OrdinalIgnoreCase));
        await using var db=new QMSPortalDbContext(new DbContextOptionsBuilder<QMSPortalDbContext>().UseSqlite(connection).Options);await db.Database.EnsureCreatedAsync();
        var department=new Department{Name="Assigned Department",NormalizedName="ASSIGNED DEPARTMENT"};db.Add(department);await db.SaveChangesAsync();
        var actor=new PortalUser{Username="actor",FullName="Actor",Role="Admin",IsActive=true,IsApproved=true};db.Add(actor);await db.SaveChangesAsync();
        var personal=new Team{Name="Personal Team",NormalizedName="PERSONAL TEAM",DepartmentId=department.Id};var foreign=new Team{Name="Foreign Team",NormalizedName="FOREIGN TEAM"};db.AddRange(personal,foreign);await db.SaveChangesAsync();
        var service=new ProfileService(db);var profile=await service.Load(actor);Assert.Null(profile.Department);Assert.Empty(profile.Teams);
        actor.Role="Manager";actor.DepartmentId=department.Id;await db.SaveChangesAsync();Assert.Empty((await service.Load(actor)).Teams);
        actor.ManagerDepartmentScope=true;await db.SaveChangesAsync();Assert.Equal("Personal Team",Assert.Single((await service.Load(actor)).Teams).Name);
        actor.Role="Team Lead";actor.ManagerDepartmentScope=false;personal.LeadUserId=actor.Id;await db.SaveChangesAsync();Assert.Equal("Personal Team",Assert.Single((await service.Load(actor)).Teams).Name);
    }
}
