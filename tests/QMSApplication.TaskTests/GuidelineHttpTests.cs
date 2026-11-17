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
public class GuidelineHttpTests
{
    [Fact] public async Task RealGuidelinePagesEnforceRolesAndRenderPerUserUnreadState()
    {
        var source=Environment.GetEnvironmentVariable("QMS_TEST_PROJECT") ?? Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(source,"QMSApplication.csproj")) && Directory.GetParent(source)!=null) source=Directory.GetParent(source)!.FullName;
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        connection.CreateCollation("ascii_general_ci",(a,b)=>string.Compare(a,b,StringComparison.OrdinalIgnoreCase));
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName=typeof(GuidelinesModel).Assembly.GetName().Name,ContentRootPath=source,EnvironmentName="Development" });
        builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<QMSPortalDbContext>(o=>o.UseSqlite(connection));
        builder.Services.AddScoped<AccountService>();builder.Services.AddScoped<AccessService>();builder.Services.AddScoped<AccessPageFilter>();
        builder.Services.AddRazorPages(o=>o.Conventions.ConfigureFilter(new ServiceFilterAttribute(typeof(AccessPageFilter)) { Order=-100 }))
            .ConfigureApplicationPartManager(m=> { if(!m.ApplicationParts.OfType<Microsoft.AspNetCore.Mvc.ApplicationParts.CompiledRazorAssemblyPart>().Any(p=>p.Assembly==typeof(GuidelinesModel).Assembly)) m.ApplicationParts.Add(new Microsoft.AspNetCore.Mvc.ApplicationParts.CompiledRazorAssemblyPart(typeof(GuidelinesModel).Assembly)); });
        builder.Services.AddDistributedMemoryCache();builder.Services.AddSession(o=> {o.Cookie.IsEssential=true;o.Cookie.SecurePolicy=CookieSecurePolicy.None;});
        await using var app=builder.Build();app.UseRouting();app.UseSession();app.UseAuthorization();app.MapRazorPages();
        long id;Guid version;
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<QMSPortalDbContext>();await db.Database.EnsureCreatedAsync();
            foreach(var (name,role) in new[] { ("admin","Admin"),("manager","Manager"),("reader","User"),("reader2","User"),("guest","Guest") })
            {
                var u=new PortalUser {Username=name,FullName=name,Role=role,IsActive=true,IsApproved=true};u.PasswordHash=new PasswordHasher<PortalUser>().HashPassword(u,"TestPassword123!");db.PortalUsers.Add(u);
            }
            await db.SaveChangesAsync();
            var admin=await db.PortalUsers.SingleAsync(u=>u.Username=="admin");
            Assert.Null(await new GuidelineService(db).Save(admin,null,Guid.Empty,"Member guidance","Original text",0,8));
            var g=await db.Guidelines.SingleAsync();id=g.Id;version=g.Version;
        }
        await app.StartAsync();var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var handler=new HttpClientHandler {AllowAutoRedirect=false,CookieContainer=new CookieContainer()};using var client=new HttpClient(handler) {BaseAddress=new Uri(address)};
        async Task<string> Get(string path) {var response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,response.StatusCode);return await response.Content.ReadAsStringAsync();}
        string Token(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        async Task Login(string name) {foreach(Cookie cookie in handler.CookieContainer.GetAllCookies()) cookie.Expired=true;var html=await Get("/Login");var response=await client.PostAsync("/Login",new FormUrlEncodedContent(new Dictionary<string,string> { ["Username"]=name,["Password"]="TestPassword123!",["__RequestVerificationToken"]=Token(html) }));Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);}
        bool Dot(string html) {var span=Regex.Match(html,"<span class=\"qms-guidelines-dot\"[^>]*>").Value;Assert.NotEmpty(span);return !Regex.IsMatch(span,@"\shidden(?:=|\s|>)");}
        await Login("reader");Assert.True(Dot(await Get("/Welcome")));
        Assert.Contains("true",await Get("/Guidelines?handler=Unread"));Assert.True(Dot(await Get("/Welcome")));
        var readerPage=await Get("/Guidelines");Assert.Contains("Original text",readerPage);Assert.DoesNotContain("Add guideline",readerPage);Assert.False(Dot(readerPage));
        var forged=await client.PostAsync("/Guidelines?handler=Delete",new FormUrlEncodedContent(new Dictionary<string,string> {["id"]=id.ToString(),["version"]=version.ToString(),["__RequestVerificationToken"]=Token(await Get("/Login"))}));
        Assert.Equal("/AccessDenied",forged.Headers.Location?.ToString());
        await Login("manager");var editor=await Get("/Guidelines?editId="+id);Assert.Contains("Save changes",editor);Assert.Contains("Target roles",editor);
        var withoutCsrf=await client.PostAsync("/Guidelines?handler=Delete",new FormUrlEncodedContent(new Dictionary<string,string>{["id"]=id.ToString(),["version"]=version.ToString()}));Assert.Equal(HttpStatusCode.BadRequest,withoutCsrf.StatusCode);
        var save=await client.PostAsync("/Guidelines?handler=Save",new FormUrlEncodedContent(new Dictionary<string,string> {
            ["Input.Id"]=id.ToString(),["Input.Version"]=version.ToString(),["Input.Title"]="Updated guidance",["Input.Content"]="<script>alert('x')</script> New text",["Input.DisplayOrder"]="0",["Input.TargetRoles"]="8",["__RequestVerificationToken"]=Token(editor) }));
        Assert.Equal(HttpStatusCode.Redirect,save.StatusCode);
        await Login("reader");Assert.True(Dot(await Get("/Welcome")));var updated=await Get("/Guidelines");Assert.Contains("New text",updated);Assert.DoesNotContain("<script>alert",updated);Assert.False(Dot(updated));
        await Login("reader2");Assert.True(Dot(await Get("/Welcome")));
        await Login("guest");Assert.False(Dot(await Get("/Welcome")));Assert.DoesNotContain("Updated guidance",await Get("/Guidelines"));
        await app.StopAsync();
    }
}
