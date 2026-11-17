using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QMSApplication.Models;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<QMSApplication.Security.DemoRegistrationStore>();
builder.Services.AddScoped<QMSApplication.Security.DemoRegistrationService>();
builder.Services.Configure<QMSApplication.Security.RegistrationEmailOptions>(builder.Configuration.GetSection("RegistrationEmail"));
builder.Services.AddScoped<QMSApplication.Security.IRegistrationEmailSender, QMSApplication.Security.SmtpRegistrationEmailSender>();
builder.Services.AddScoped<QMSApplication.Security.RegistrationOtpService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<QMSApplication.Data.TeamSetupService>();
builder.Services.AddDbContext<QMSPortalDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("QMSPortalDb"),
        ServerVersion.AutoDetect(
            builder.Configuration.GetConnectionString("QMSPortalDb"))));
builder.Services.AddScoped<QMSApplication.Data.MDExcelImporter>();
// Add Razor Pages
builder.Services.AddScoped<QMSApplication.Security.AccessService>();
builder.Services.AddScoped<QMSApplication.Security.AccessPageFilter>();
builder.Services.AddScoped<QMSApplication.Security.AccountService>();
builder.Services.AddRazorPages(o => {
    o.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.ServiceFilterAttribute(typeof(QMSApplication.Security.AccessPageFilter)) { Order = -100 });
    o.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.ServiceFilterAttribute(typeof(QMSApplication.Filters.MandatoryNotificationFilter)));
});
builder.Services.AddRateLimiter(o => {
    o.RejectionStatusCode = 429;
    o.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(c => {
        var path = c.Request.Path.Value?.ToLowerInvariant() ?? "";
        return new[] { "/login", "/register", "/verifyregistration", "/forgotpassword", "/resetpassword" }.Contains(path) && HttpMethods.IsPost(c.Request.Method)
            ? System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
            : System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("pages");
    });
});
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.CheckConsentNeeded = context => true;
    options.MinimumSameSitePolicy = SameSiteMode.Lax;
});
builder.Services.AddScoped<QMSApplication.Filters.MandatoryNotificationFilter>();

// Add Session
builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);

    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.Configure<FormOptions>(options =>
{
    options.ValueCountLimit = 10000;
});

var app = builder.Build();


if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();
app.UseRateLimiter();
app.UseCookiePolicy();
// Enable Session
app.UseSession();

app.UseAuthorization();

app.MapRazorPages();

app.Run();