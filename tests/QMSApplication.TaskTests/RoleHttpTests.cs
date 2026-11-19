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
using QMSApplication.Data;
using QMSApplication.Models;
using QMSApplication.Pages;
using QMSApplication.Security;
using Xunit;

namespace QMSApplication.TaskTests;
public class RoleHttpTests
{
    [Fact]
    public async Task RealPagesUseSessionFiltersAntiforgeryAndScopedForms()
    {
        var source = Environment.GetEnvironmentVariable("QMS_TEST_PROJECT") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        // Locate the source project when invoked by dotnet test in the supplied solution.
        if (!File.Exists(Path.Combine(source, "QMSApplication.csproj")))
        {
            source = Directory.GetCurrentDirectory();
            while (!File.Exists(Path.Combine(source, "QMSApplication.csproj")) && Directory.GetParent(source) != null)
                source = Directory.GetParent(source)!.FullName;
        }

        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        connection.CreateCollation("ascii_general_ci", (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(MDTasksModel).Assembly.GetName().Name, ContentRootPath = source, EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<QMSPortalDbContext>(o => o.UseSqlite(connection));
        var registrationMail = new TestRegistrationEmailSender();
        builder.Services.AddSingleton<IRegistrationEmailSender>(registrationMail);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<RegistrationOtpService>();
        builder.Services.AddScoped<AccessService>();
        builder.Services.AddScoped<AccountService>();
        builder.Services.AddScoped<TeamSetupService>();
        builder.Services.AddScoped<MDExcelImporter>();
        builder.Services.AddScoped<AccessPageFilter>();
        builder.Services.AddScoped<QMSApplication.Filters.MandatoryNotificationFilter>();
        builder.Services.AddRazorPages(o =>
        {
            o.Conventions.ConfigureFilter(new ServiceFilterAttribute(typeof(AccessPageFilter)) { Order = -100 });
            o.Conventions.ConfigureFilter(new ServiceFilterAttribute(typeof(QMSApplication.Filters.MandatoryNotificationFilter)));
        }).ConfigureApplicationPartManager(m =>
        {
            if (!m.ApplicationParts.OfType<Microsoft.AspNetCore.Mvc.ApplicationParts.CompiledRazorAssemblyPart>().Any(p => p.Assembly == typeof(MDTasksModel).Assembly))
                m.ApplicationParts.Add(new Microsoft.AspNetCore.Mvc.ApplicationParts.CompiledRazorAssemblyPart(typeof(MDTasksModel).Assembly));
        });
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession(o =>
        {
            o.Cookie.IsEssential = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.None;
        }); // HTTP loopback test host only.
        await using var app = builder.Build();
        app.UseRouting();
        app.UseSession();
        app.UseAuthorization();
        app.MapRazorPages();
        long ownId, otherId, memberId;
        Guid memberVersion;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QMSPortalDbContext>();
            await db.Database.EnsureCreatedAsync();
            foreach (var pair in new[]
            {
                ("admin", "Admin"),
                ("manager", "Manager"),
                ("guest", "Guest"),
                ("member", "User")
            }

            )
            {
                var u = new PortalUser
                {
                    Username = pair.Item1,
                    FullName = pair.Item1,
                    Role = pair.Item2
                };
                u.PasswordHash = new PasswordHasher<PortalUser>().HashPassword(u, "TestPassword123!");
                db.Add(u);
            }

            await db.SaveChangesAsync();
            var a = new Team
            {
                Name = "Own",
                NormalizedName = "OWN",
                CreatedBy = "admin"
            };
            var b = new Team
            {
                Name = "Other",
                NormalizedName = "OTHER",
                CreatedBy = "admin"
            };
            db.AddRange(a, b);
            await db.SaveChangesAsync();
            var manager = await db.PortalUsers.SingleAsync(x => x.Username == "manager");
            var member = await db.PortalUsers.SingleAsync(x => x.Username == "member");
            memberId = member.Id;
            memberVersion = member.Version;
            db.Add(new TeamManager { TeamId = a.Id, UserId = manager.Id });
            db.Add(new TeamMembership { TeamId = a.Id, UserId = member.Id });
            var own = new MDEntity
            {
                RecordID = "MD-OWN",
                DeliverableName = "OWN-ONLY-TEXT",
                MilestoneName = "Release",
                TeamId = a.Id,
                AssignedTo = "member",
                DeliverableType = "UGs",
                Status = "Not Started",
                DepartmentName = "Labeling",
                MilestoneTargetDate = new DateOnly(2026, 11, 1),
                DeliverableTargetDate = new DateOnly(2026, 11, 2)
            };
            var other = new MDEntity
            {
                RecordID = "MD-OTHER",
                DeliverableName = "FOREIGN-SECRET-TEXT",
                MilestoneName = "Other",
                TeamId = b.Id,
                AssignedTo = "guest",
                Status = "Not Started"
            };
            db.AddRange(own, other);
            await db.SaveChangesAsync();
            ownId = own.Id;
            otherId = other.Id;
        }

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            CookieContainer = new CookieContainer()
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(address)
        };
        async Task<string> Get(string url)
        {
            var r = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            return await r.Content.ReadAsStringAsync();
        }

        string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        async Task Login(string username)
        {
            var login = await Get("/Login");
            var r = await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string> { { "Username", username }, { "Password", "TestPassword123!" }, { "__RequestVerificationToken", Token(login) } }));
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.Equal("/Welcome", r.Headers.Location?.ToString());
        }

        async Task Denied(string url)
        {
            var r = await client.GetAsync(url);
            Assert.True(r.Headers.Location?.ToString() == "/AccessDenied", $"{url} returned {r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        }

        var anonymous = await client.GetAsync("/GlobalSearch?Query=Release");
        Assert.True(anonymous.Headers.Location?.ToString() == "/Login", $"Anonymous search returned {anonymous.StatusCode}: {await anonymous.Content.ReadAsStringAsync()}");
        await Login("guest");
        Assert.Contains("Continue to Dashboard", await Get("/Welcome"));
        Assert.Contains("FOREIGN-SECRET-TEXT", await Get("/Reports"));
        await Denied("/MDCreate");
        await Denied("/ManageUsers");
        var guestMd = await Get("/md");
        var forged = await client.PostAsync("/md?handler=Delete&id=MD-OWN", new FormUrlEncodedContent(new Dictionary<string, string> { { "__RequestVerificationToken", Token(guestMd) } }));
        Assert.True(forged.Headers.Location?.ToString() == "/AccessDenied", $"Guest delete returned {forged.StatusCode}: {await forged.Content.ReadAsStringAsync()}");
        Assert.DoesNotContain("<span>Manage Users</span>", await Get("/Dashboard"));
        await Login("manager");
        var md = await Get("/md");
        Assert.Contains("OWN-ONLY-TEXT", md);
        Assert.DoesNotContain("FOREIGN-SECRET-TEXT", md);
        Assert.Contains("MDEdit", md);
        Assert.DoesNotContain("FOREIGN-SECRET-TEXT", await Get("/Reports"));
        await Denied($"/MDTasks/{otherId}");
        Assert.Contains("Save changes", await Get("/MDEdit?id=MD-OWN"));
        Assert.Contains("Choose owning team", await Get("/MDCreate"), StringComparison.OrdinalIgnoreCase);
        await Login("admin");
        var users = await Get("/ManageUsers");
        Assert.Contains("Manage Users", users);
        Assert.DoesNotContain("Create password reset link", users);
        Assert.Contains("email OTP", users);
        Assert.Contains("<span>Manage Users</span>", users);
        var badCsrf = await client.PostAsync("/ManageUsers?handler=Save", new FormUrlEncodedContent(new Dictionary<string, string> { { "userId", memberId.ToString() } }));
        Assert.Equal(HttpStatusCode.BadRequest, badCsrf.StatusCode);
        var saved = await client.PostAsync("/ManageUsers?handler=Save", new FormUrlEncodedContent(new Dictionary<string, string> { { "userId", memberId.ToString() }, { "version", memberVersion.ToString() }, { "role", "Guest" }, { "approved", "true" }, { "active", "true" }, { "reason", "Reviewed role" }, { "__RequestVerificationToken", Token(users) } }));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QMSPortalDbContext>();
            Assert.Equal("Guest", (await db.PortalUsers.FindAsync(memberId))!.Role);
            Assert.True(await db.MDRecords.AnyAsync(x => x.Id == ownId));
        }

        var registration = await Get("/register?submitted=true");
        Assert.Contains("Send verification code", registration); Assert.DoesNotContain("Registration received", registration);
        var sent = await client.PostAsync("/register", new FormUrlEncodedContent(new Dictionary<string,string> {
            { "FirstName", "Email" }, { "LastName", "Person" }, { "Username", "emailperson" }, { "Email", "emailperson@example.test" },
            { "MobileNumber", "9001234567" }, { "Password", "EmailPassword123!" }, { "Role", "Admin" }, { "__RequestVerificationToken", Token(registration) }
        }));
        Assert.Equal("/VerifyRegistration", sent.Headers.Location?.ToString());
        using (var scope = app.Services.CreateScope()) Assert.False(await scope.ServiceProvider.GetRequiredService<QMSPortalDbContext>().PortalUsers.AnyAsync(x => x.Username == "emailperson"));
        var verify = await Get("/VerifyRegistration"); Assert.Contains("Verify your email", verify);
        Assert.DoesNotContain(registrationMail.Messages.Last().Code, verify);
        var wrong = registrationMail.Messages.Last().Code == "123456" ? "654321" : "123456";
        var rejected = await client.PostAsync("/VerifyRegistration", new FormUrlEncodedContent(new Dictionary<string,string> { { "Code", wrong }, { "__RequestVerificationToken", Token(verify) } }));
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode); Assert.Contains("Incorrect code", await rejected.Content.ReadAsStringAsync());
        var completed = await client.PostAsync("/VerifyRegistration", new FormUrlEncodedContent(new Dictionary<string,string> { { "Code", registrationMail.Messages.Last().Code }, { "__RequestVerificationToken", Token(verify) } }));
        Assert.Equal("/register", completed.Headers.Location?.ToString());
        using (var scope = app.Services.CreateScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<QMSPortalDbContext>().PortalUsers.SingleAsync(x => x.Username == "emailperson");
            Assert.Equal("User", user.Role); Assert.False(user.IsApproved); Assert.False(user.IsActive);
        }
        Assert.Contains("Your OTP check is complete", await Get("/register"));
        await app.StopAsync();
    }
}
