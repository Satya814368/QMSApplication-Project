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
public class PasswordRecoveryHttpTests
{
    [Fact] public async Task SelfServiceOtpResetsPasswordWithoutAdminAndRejectsCsrfReplayAndOldSessions()
    {
        var source=Environment.GetEnvironmentVariable("QMS_TEST_PROJECT") ?? Directory.GetCurrentDirectory();
        while(!File.Exists(Path.Combine(source,"QMSApplication.csproj"))&&Directory.GetParent(source)!=null)source=Directory.GetParent(source)!.FullName;
        await using var connection=new SqliteConnection("Data Source=:memory:;Foreign Keys=True");await connection.OpenAsync();connection.CreateCollation("ascii_general_ci",(a,b)=>string.Compare(a,b,StringComparison.OrdinalIgnoreCase));
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ApplicationName=typeof(LoginModel).Assembly.GetName().Name,ContentRootPath=source,EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        var mail=new PasswordRecoveryOtpTests.Mail();builder.Services.AddSingleton<IRegistrationEmailSender>(mail);builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddDbContext<QMSPortalDbContext>(o=>o.UseSqlite(connection));builder.Services.AddScoped<PasswordRecoveryOtpService>();builder.Services.AddScoped<AccountService>();builder.Services.AddScoped<AccessService>();builder.Services.AddScoped<AccessPageFilter>();builder.Services.AddScoped<QMSApplication.Filters.MandatoryNotificationFilter>();
        builder.Services.AddRazorPages(o=>{o.Conventions.ConfigureFilter(new ServiceFilterAttribute(typeof(AccessPageFilter)){Order=-100});o.Conventions.ConfigureFilter(new ServiceFilterAttribute(typeof(QMSApplication.Filters.MandatoryNotificationFilter)));}).ConfigureApplicationPartManager(m=>{if(!m.ApplicationParts.OfType<Microsoft.AspNetCore.Mvc.ApplicationParts.CompiledRazorAssemblyPart>().Any(p=>p.Assembly==typeof(LoginModel).Assembly))m.ApplicationParts.Add(new Microsoft.AspNetCore.Mvc.ApplicationParts.CompiledRazorAssemblyPart(typeof(LoginModel).Assembly));});
        builder.Services.AddDistributedMemoryCache();builder.Services.AddSession(o=>{o.Cookie.IsEssential=true;o.Cookie.SecurePolicy=CookieSecurePolicy.None;});
        await using var app=builder.Build();app.UseRouting();app.UseSession();app.UseAuthorization();app.MapRazorPages();
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<QMSPortalDbContext>();await db.Database.EnsureCreatedAsync();var user=new PortalUser{Username="person",FullName="Person",Email="person@example.test",Role="User",IsApproved=true,IsActive=true};user.PasswordHash=new PasswordHasher<PortalUser>().HashPassword(user,"OldPassword12345!");var admin=new PortalUser{Username="admin",Role="Admin",FullName="Admin",IsApproved=true,IsActive=true};admin.PasswordHash=new PasswordHasher<PortalUser>().HashPassword(admin,"AdminPassword123!");db.AddRange(user,admin);await db.SaveChangesAsync();db.Add(new Notification{Title="Pending",Message="Notice",IsPublished=true,TargetRole="User"});await db.SaveChangesAsync();}
        await app.StartAsync();var url=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new CookieContainer()}){BaseAddress=new Uri(url)};
        using var oldSession=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new CookieContainer()}){BaseAddress=new Uri(url)};
        static string Token(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        async Task<string> Get(HttpClient c,string path){var r=await c.GetAsync(path);Assert.Equal(HttpStatusCode.OK,r.StatusCode);return await r.Content.ReadAsStringAsync();}
        async Task<HttpResponseMessage> Post(HttpClient c,string path,Dictionary<string,string> values,string token){values["__RequestVerificationToken"]=token;return await c.PostAsync(path,new FormUrlEncodedContent(values));}
        var login=await Get(oldSession,"/Login");Assert.Equal(HttpStatusCode.Redirect,(await Post(oldSession,"/Login",new(){["Username"]="person",["Password"]="OldPassword12345!"},Token(login))).StatusCode);
        var direct=await client.GetAsync("/VerifyPasswordOtp");Assert.Equal("/forgotpassword",direct.Headers.Location?.ToString());
        var forgot=await Get(client,"/ForgotPassword");var bad=await client.PostAsync("/ForgotPassword",new FormUrlEncodedContent(new Dictionary<string,string>{{"Email","person@example.test"}}));Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);Assert.Equal(0,mail.Sent);
        var sent=await Post(client,"/ForgotPassword",new(){["Email"]="person@example.test"},Token(forgot));Assert.Equal("/VerifyPasswordOtp",sent.Headers.Location?.ToString());Assert.Equal(1,mail.Sent);
        var verify=await Get(client,"/VerifyPasswordOtp");Assert.Contains("Verify OTP and reset password",verify);Assert.DoesNotContain(mail.Code,verify);
        var wrong=mail.Code=="000000"?"111111":"000000";
        var failed=await Post(client,"/VerifyPasswordOtp",new(){["Code"]=wrong,["Password"]="NewPassword12345!",["ConfirmPassword"]="NewPassword12345!"},Token(verify));Assert.Equal(HttpStatusCode.OK,failed.StatusCode);var failedHtml=await failed.Content.ReadAsStringAsync();Assert.Contains("Invalid or expired",failedHtml);Assert.DoesNotContain("value=\"NewPassword12345!\"",failedHtml);Assert.DoesNotContain("value=\""+wrong+"\"",failedHtml);
        var done=await Post(client,"/VerifyPasswordOtp",new(){["Code"]=mail.Code,["Password"]="NewPassword12345!",["ConfirmPassword"]="NewPassword12345!"},Token(failedHtml));Assert.Contains("Password updated successfully",await done.Content.ReadAsStringAsync());
        var invalidated=await oldSession.GetAsync("/MyTasks");Assert.Equal("/Login",invalidated.Headers.Location?.ToString());
        var replay=await Post(client,"/VerifyPasswordOtp",new(){["Code"]=mail.Code,["Password"]="ReplayPassword12345!",["ConfirmPassword"]="ReplayPassword12345!"},Token(failedHtml));Assert.Equal("/forgotpassword",replay.Headers.Location?.ToString());
        var legacy=await client.GetAsync("/ResetPassword?token=old-link");Assert.Equal("/forgotpassword",legacy.Headers.Location?.ToString());
        login=await Get(client,"/Login");Assert.Equal(HttpStatusCode.Redirect,(await Post(client,"/Login",new(){["Username"]="person",["Password"]="NewPassword12345!"},Token(login))).StatusCode);
        login=await Get(oldSession,"/Login");Assert.Equal(HttpStatusCode.Redirect,(await Post(oldSession,"/Login",new(){["Username"]="admin",["Password"]="AdminPassword123!"},Token(login))).StatusCode);
        var manage=await Get(oldSession,"/ManageUsers");Assert.DoesNotContain("Create password reset link",manage);Assert.Contains("email OTP",manage);
        await app.StopAsync();
    }
}
