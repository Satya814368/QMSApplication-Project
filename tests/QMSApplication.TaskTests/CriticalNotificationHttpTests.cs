using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QMSApplication.Models;
using QMSApplication.Pages;
using QMSApplication.Security;
using Xunit;
namespace QMSApplication.TaskTests;
public class CriticalNotificationHttpTests
{
    [Fact] public async Task ReportsEnforceTeamScopeAndEveryPublishedNotificationRequiresAcknowledgement()
    {
        var source=Environment.GetEnvironmentVariable("QMS_TEST_PROJECT") ?? Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(source,"QMSApplication.csproj")) && Directory.GetParent(source)!=null) source=Directory.GetParent(source)!.FullName;
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        connection.CreateCollation("ascii_general_ci",(a,b)=>string.Compare(a,b,StringComparison.OrdinalIgnoreCase));
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName=typeof(GuidelinesModel).Assembly.GetName().Name,ContentRootPath=source,EnvironmentName="Development" });
        builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<QMSPortalDbContext>(o=>o.UseSqlite(connection));
        builder.Services.AddScoped<QMSApplication.Filters.MandatoryNotificationFilter>();builder.Services.AddScoped<AccountService>();builder.Services.AddScoped<AccessService>();builder.Services.AddScoped<AccessPageFilter>();
        builder.Services.AddRazorPages(o=> {o.Conventions.ConfigureFilter(new ServiceFilterAttribute(typeof(AccessPageFilter)) { Order=-100 });o.Conventions.ConfigureFilter(new ServiceFilterAttribute(typeof(QMSApplication.Filters.MandatoryNotificationFilter)));})
            .ConfigureApplicationPartManager(m=> { if(!m.ApplicationParts.OfType<Microsoft.AspNetCore.Mvc.ApplicationParts.CompiledRazorAssemblyPart>().Any(p=>p.Assembly==typeof(GuidelinesModel).Assembly)) m.ApplicationParts.Add(new Microsoft.AspNetCore.Mvc.ApplicationParts.CompiledRazorAssemblyPart(typeof(GuidelinesModel).Assembly)); });
        builder.Services.AddDistributedMemoryCache();builder.Services.AddSession(o=> {o.Cookie.IsEssential=true;o.Cookie.SecurePolicy=CookieSecurePolicy.None;});
        await using var app=builder.Build();app.UseRouting();app.UseSession();app.UseAuthorization();app.MapRazorPages();
        long noticeId,departmentId,foreignTeamId;
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<QMSPortalDbContext>();await db.Database.EnsureCreatedAsync();
            var dept=new Department{Name="Labelling",NormalizedName="LABELLING"};var other=new Department{Name="Software",NormalizedName="SOFTWARE"};db.AddRange(dept,other);await db.SaveChangesAsync();departmentId=other.Id;
            var users=new Dictionary<string,PortalUser>();
            foreach(var (name,role) in new[]{("admin","Admin"),("manager","Manager"),("lead","Team Lead"),("TEAM-A-MEMBER","User"),("TEAM-B-SECRET","User"),("guest","Guest")})
            {
                var u=new PortalUser{Username=name,FullName=name,Role=role,IsActive=true,IsApproved=true,DepartmentId=name=="TEAM-B-SECRET"?other.Id:dept.Id};
                u.PasswordHash=new PasswordHasher<PortalUser>().HashPassword(u,"TestPassword123!");users[name]=u;db.Add(u);
            }
            await db.SaveChangesAsync();var a=new Team{Name="Team A",NormalizedName="TEAM A",DepartmentId=dept.Id,LeadUserId=users["lead"].Id};var b=new Team{Name="Team B",NormalizedName="TEAM B",DepartmentId=other.Id};db.AddRange(a,b);await db.SaveChangesAsync();foreignTeamId=b.Id;
            db.TeamManagers.Add(new TeamManager{TeamId=a.Id,UserId=users["manager"].Id});db.TeamMemberships.Add(new TeamMembership{TeamId=a.Id,UserId=users["TEAM-A-MEMBER"].Id});db.TeamMemberships.Add(new TeamMembership{TeamId=b.Id,UserId=users["TEAM-B-SECRET"].Id});
            var notice=new Notification{Title="Notice",Message="Team update",IsPublished=true,TargetRole="User"};db.Add(notice);await db.SaveChangesAsync();noticeId=notice.Id;
            db.NotificationUserStatuses.Add(new NotificationUserStatus{NotificationId=notice.Id,Username="TEAM-A-MEMBER",IsAcknowledged=true,AcknowledgedAtUtc=DateTime.UtcNow});await db.SaveChangesAsync();
        }
        await app.StartAsync();var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var handler=new HttpClientHandler {AllowAutoRedirect=false,CookieContainer=new CookieContainer()};using var client=new HttpClient(handler) {BaseAddress=new Uri(address)};
        async Task<string> Get(string path) {var response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,response.StatusCode);return await response.Content.ReadAsStringAsync();}
        string Token(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        async Task Login(string name) {foreach(Cookie cookie in handler.CookieContainer.GetAllCookies()) cookie.Expired=true;var html=await Get("/Login");var response=await client.PostAsync("/Login",new FormUrlEncodedContent(new Dictionary<string,string> { ["Username"]=name,["Password"]="TestPassword123!",["__RequestVerificationToken"]=Token(html) }));Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);}
        await Login("manager");var report=await Get("/NotificationAdmin?handler=Acknowledgements&id="+noticeId);
        Assert.DoesNotContain("<html",report);Assert.Contains("TEAM-A-MEMBER",report);Assert.DoesNotContain("TEAM-B-SECRET",report);Assert.DoesNotContain("id=\"ack-department\"",report);
        var denied=await client.GetAsync("/NotificationAdmin?handler=Acknowledgements&id="+noticeId+"&teamId="+foreignTeamId);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        var editor=await Get("/NotificationCreate");Assert.DoesNotContain("name=\"Priority\"",editor);Assert.DoesNotContain("name=\"IsMandatory\"",editor);
        var created=await client.PostAsync("/NotificationCreate",new FormUrlEncodedContent(new Dictionary<string,string>{["Title"]="HTTP critical",["Message"]="Text",["Priority"]="Normal",["IsMandatory"]="false",["IsPublished"]="true",["TargetRole"]="User",["__RequestVerificationToken"]=Token(editor)}));
        Assert.Equal(HttpStatusCode.Redirect,created.StatusCode);
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<QMSPortalDbContext>();Assert.NotNull(await db.Notifications.SingleAsync(n=>n.Title=="HTTP critical"));Assert.Null(db.Model.FindEntityType(typeof(Notification))!.FindProperty("Priority"));Assert.Null(db.Model.FindEntityType(typeof(Notification))!.FindProperty("IsMandatory"));}
        await Login("lead");var management=await Get("/NotificationAdmin");Assert.Contains("Track acknowledgements",management);Assert.Contains("acknowledgements-dialog",management);Assert.DoesNotContain("Create notification</a>",management);Assert.DoesNotContain("Notification tracking",management);var lead=await Get("/NotificationAdmin?handler=Acknowledgements&id="+noticeId);Assert.Contains("TEAM-A-MEMBER",lead);Assert.DoesNotContain("TEAM-B-SECRET",lead);
        var forbidden=await client.GetAsync("/NotificationCreate");Assert.Equal("/AccessDenied",forbidden.Headers.Location?.ToString());
        await Login("admin");var admin=await Get("/NotificationAdmin?handler=Acknowledgements&id="+noticeId);Assert.Contains("Labelling",admin);Assert.Contains("Software",admin);Assert.Contains("TEAM-A-MEMBER",admin);Assert.Contains("TEAM-B-SECRET",admin);
        var filtered=await Get("/NotificationAdmin?handler=Acknowledgements&id="+noticeId+"&DepartmentId="+departmentId);Assert.Contains("TEAM-B-SECRET",filtered);Assert.DoesNotContain("TEAM-A-MEMBER",filtered);
        await Login("TEAM-B-SECRET");var blocked=await client.GetAsync("/Welcome");Assert.Equal("/Notifications",blocked.Headers.Location?.ToString());var inbox=await Get("/Notifications");Assert.Contains("Acknowledge",inbox);Assert.DoesNotContain("class=\"priority\"",inbox);
        var afterRead=await client.GetAsync("/Welcome");Assert.Equal("/Notifications",afterRead.Headers.Location?.ToString());
        var ack=await client.PostAsync("/Notifications?handler=Acknowledge",new FormUrlEncodedContent(new Dictionary<string,string>{["id"]=noticeId.ToString(),["__RequestVerificationToken"]=Token(inbox)}));Assert.Equal(HttpStatusCode.Redirect,ack.StatusCode);
        var stillPending=await client.GetAsync("/Welcome");Assert.Equal("/Notifications",stillPending.Headers.Location?.ToString());
        long secondId;using(var scope=app.Services.CreateScope()){secondId=(await scope.ServiceProvider.GetRequiredService<QMSPortalDbContext>().Notifications.SingleAsync(n=>n.Title=="HTTP critical")).Id;}
        var secondAck=await client.PostAsync("/Notifications?handler=Acknowledge",new FormUrlEncodedContent(new Dictionary<string,string>{["id"]=secondId.ToString(),["__RequestVerificationToken"]=Token(inbox)}));Assert.Equal(HttpStatusCode.Redirect,secondAck.StatusCode);
        Assert.Contains("Welcome",await Get("/Welcome"));
        var readerDenied=await client.GetAsync("/NotificationAdmin");Assert.Equal("/AccessDenied",readerDenied.Headers.Location?.ToString());
        await Login("admin");filtered=await Get("/NotificationAdmin?handler=Acknowledgements&id="+noticeId+"&DepartmentId="+departmentId);Assert.Contains("Acknowledged: 1",filtered);
        await app.StopAsync();
    }
}
