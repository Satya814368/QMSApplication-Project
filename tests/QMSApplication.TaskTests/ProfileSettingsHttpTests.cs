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
public class ProfileSettingsHttpTests
{
    [Theory]
    [InlineData("Admin")][InlineData("Manager")][InlineData("Team Lead")][InlineData("User")][InlineData("Guest")]
    public async Task PersonalPagesStayPrivateAndOwnPasswordCanBeChangedByEveryRole(string role)
    {
        var source=Environment.GetEnvironmentVariable("QMS_TEST_PROJECT") ?? Directory.GetCurrentDirectory();
        while(!File.Exists(Path.Combine(source,"QMSApplication.csproj"))&&Directory.GetParent(source)!=null)source=Directory.GetParent(source)!.FullName;
        await using var connection=new SqliteConnection("Data Source=:memory:;Foreign Keys=True");await connection.OpenAsync();connection.CreateCollation("ascii_general_ci",(a,b)=>string.Compare(a,b,StringComparison.OrdinalIgnoreCase));
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ApplicationName=typeof(LoginModel).Assembly.GetName().Name,ContentRootPath=source,EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        var mail=new ProfileMail();builder.Services.AddSingleton<IRegistrationEmailSender>(mail);builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddDbContext<QMSPortalDbContext>(o=>o.UseSqlite(connection));builder.Services.AddScoped<PasswordRecoveryOtpService>();builder.Services.AddScoped<AccountService>();builder.Services.AddScoped<AccessService>();builder.Services.AddScoped<AccessPageFilter>();builder.Services.AddScoped<QMSApplication.Filters.MandatoryNotificationFilter>();
        builder.Services.AddRazorPages(o=>{o.Conventions.ConfigureFilter(new ServiceFilterAttribute(typeof(AccessPageFilter)){Order=-100});o.Conventions.ConfigureFilter(new ServiceFilterAttribute(typeof(QMSApplication.Filters.MandatoryNotificationFilter)));}).ConfigureApplicationPartManager(m=>{if(!m.ApplicationParts.OfType<Microsoft.AspNetCore.Mvc.ApplicationParts.CompiledRazorAssemblyPart>().Any(p=>p.Assembly==typeof(LoginModel).Assembly))m.ApplicationParts.Add(new Microsoft.AspNetCore.Mvc.ApplicationParts.CompiledRazorAssemblyPart(typeof(LoginModel).Assembly));});
        builder.Services.AddDistributedMemoryCache();builder.Services.AddSession(o=>{o.Cookie.IsEssential=true;o.Cookie.SecurePolicy=CookieSecurePolicy.None;});
        await using var app=builder.Build();app.UseRouting();app.UseSession();app.UseAuthorization();app.MapRazorPages();
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<QMSPortalDbContext>();await db.Database.EnsureCreatedAsync();
            var dept=new Department{Name="My Department",NormalizedName="MY DEPARTMENT"};db.Add(dept);await db.SaveChangesAsync();
            var users=new Dictionary<string,PortalUser>();
            foreach(var name in new[]{"person","other","lead","manager"}){
                var user=new PortalUser{Username=name,FullName=name=="person"?"CURRENT USER":name=="other"?"PRIVATE-OTHER":name=="lead"?"Lead Contact":"Manager Contact",Email=name+"@example.test",MobileNumber=name=="person"?"9001234567":"",Role=name=="person"?role:name=="lead"?"Team Lead":name=="manager"?"Manager":"User",DepartmentId=dept.Id,IsApproved=true,IsActive=true};user.PasswordHash=new PasswordHasher<PortalUser>().HashPassword(user,"OldPassword12345!");db.Add(user);users[name]=user;
            }
            await db.SaveChangesAsync();var team=new Team{Name="Assigned Team",NormalizedName="ASSIGNED TEAM",DepartmentId=dept.Id,LeadUserId=users["lead"].Id};var otherTeam=new Team{Name="PRIVATE OTHER TEAM",NormalizedName="PRIVATE OTHER TEAM"};db.AddRange(team,otherTeam);await db.SaveChangesAsync();
            db.TeamManagers.Add(new TeamManager{TeamId=team.Id,UserId=users["manager"].Id});
            if(role=="Manager")db.TeamManagers.Add(new TeamManager{TeamId=team.Id,UserId=users["person"].Id});
            else db.TeamMemberships.Add(new TeamMembership{TeamId=team.Id,UserId=users["person"].Id});await db.SaveChangesAsync();
        }
        await app.StartAsync();var url=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new CookieContainer()}){BaseAddress=new Uri(url)};
        using var oldSession=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new CookieContainer()}){BaseAddress=new Uri(url)};
        static string Token(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        async Task<string> Get(HttpClient c,string path){var r=await c.GetAsync(path);Assert.Equal(HttpStatusCode.OK,r.StatusCode);return await r.Content.ReadAsStringAsync();}
        async Task<HttpResponseMessage> Post(HttpClient c,string path,Dictionary<string,string> values,string token){values["__RequestVerificationToken"]=token;return await c.PostAsync(path,new FormUrlEncodedContent(values));}
        Assert.Equal("/Login",(await client.GetAsync("/MyProfile")).Headers.Location?.ToString());
        foreach(var browser in new[]{client,oldSession}){var login=await Get(browser,"/Login");Assert.Equal(HttpStatusCode.Redirect,(await Post(browser,"/Login",new(){["Username"]="person",["Password"]="OldPassword12345!"},Token(login))).StatusCode);}
        var profile=await Get(client,"/MyProfile?userId=2&Username=other");Assert.Contains("person@example.test",profile);Assert.Contains("9001234567",profile);Assert.DoesNotContain("PRIVATE-OTHER",profile);Assert.DoesNotContain("other@example.test",profile);Assert.Contains("profile-menu-button",profile);Assert.Contains("My Team &amp; Department",profile);
        var teams=await Get(client,"/MyProfileTeam?userId=2");Assert.Contains("Assigned Team",teams);Assert.Contains("Lead Contact",teams);Assert.Contains("Manager Contact",teams);Assert.DoesNotContain("PRIVATE OTHER TEAM",teams);
        var password=await Get(client,"/ChangePassword");Assert.Contains("p***@example.test",password);Assert.DoesNotContain("name=\"Email\"",password);
        var csrf=await client.PostAsync("/ChangePassword?handler=Send",new FormUrlEncodedContent(new Dictionary<string,string>()));Assert.Equal(HttpStatusCode.BadRequest,csrf.StatusCode);Assert.Equal(0,mail.Sent);
        var sent=await Post(client,"/ChangePassword?handler=Send",new(){["Email"]="other@example.test",["userId"]="2"},Token(password));Assert.Equal(HttpStatusCode.Redirect,sent.StatusCode);Assert.Equal("person@example.test",mail.Recipient);
        var verify=await Get(client,"/ChangePassword");Assert.Contains("Verify OTP &amp; change password",verify);Assert.DoesNotContain(mail.Code,verify);
        var wrong=mail.Code=="000000"?"111111":"000000";
        var failed=await Post(client,"/ChangePassword?handler=Verify",new(){["Code"]=wrong,["Password"]="NewPassword12345!",["ConfirmPassword"]="NewPassword12345!"},Token(verify));var html=await failed.Content.ReadAsStringAsync();Assert.Contains("Invalid or expired",html);Assert.DoesNotContain("value=\"NewPassword12345!\"",html);
        var done=await Post(client,"/ChangePassword?handler=Verify",new(){["Code"]=mail.Code,["Password"]="NewPassword12345!",["ConfirmPassword"]="NewPassword12345!"},Token(html));Assert.Equal(HttpStatusCode.OK,done.StatusCode);Assert.Contains("Your password has been updated",await done.Content.ReadAsStringAsync());
        Assert.Equal("/Login",(await oldSession.GetAsync("/MyProfile")).Headers.Location?.ToString());Assert.Equal("/Login",(await client.GetAsync("/MyProfile")).Headers.Location?.ToString());
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<QMSPortalDbContext>();var other=await db.PortalUsers.SingleAsync(u=>u.Username=="other");Assert.NotEqual(PasswordVerificationResult.Failed,new PasswordHasher<PortalUser>().VerifyHashedPassword(other,other.PasswordHash!,"OldPassword12345!"));}
        var signin=await Get(client,"/Login");Assert.Equal(HttpStatusCode.Redirect,(await Post(client,"/Login",new(){["Username"]="person",["Password"]="NewPassword12345!"},Token(signin))).StatusCode);
        profile=await Get(client,"/MyProfile");Assert.Equal(HttpStatusCode.Redirect,(await Post(client,"/Dashboard?handler=Logout",new(),Token(profile))).StatusCode);Assert.Equal("/Login",(await client.GetAsync("/MyProfile")).Headers.Location?.ToString());
        await app.StopAsync();
    }
    private sealed class ProfileMail : IRegistrationEmailSender
    {
        public bool IsConfigured=>true;public string Code="",Recipient="";public int Sent;
        public Task SendAsync(string recipient,string code,CancellationToken ct=default)=>Task.CompletedTask;
        public Task SendPasswordResetAsync(string recipient,string code,CancellationToken ct=default){Recipient=recipient;Code=code;Sent++;return Task.CompletedTask;}
    }
}
