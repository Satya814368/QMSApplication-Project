using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QMSApplication.Models;
using QMSApplication.Security;
using Xunit;
namespace QMSApplication.TaskTests;
public class DemoRegistrationTests
{
    private static (SmtpRegistrationEmailSender Sender, HttpContextAccessor Http, Options Options) Sender(string environment)
    {
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { Session = new MemorySession() } };
        var options = new Options();
        return (new(options, NullLogger<SmtpRegistrationEmailSender>.Instance, new EnvironmentStub { EnvironmentName = environment }, http), http, options);
    }
    [Fact] public async Task DemoCodeStaysInInitiatingSessionAndNeverNeedsSmtp()
    {
        var (sender, http, _) = Sender("Development"); Assert.True(sender.IsDemo); Assert.True(sender.IsConfigured);
        await sender.SendAsync("test@example.test", "123456"); Assert.Equal("123456", sender.DemoCode);
        http.HttpContext = new DefaultHttpContext { Session = new MemorySession() }; Assert.Null(sender.DemoCode);
    }
    [Fact] public async Task ProductionRefusesDemoEvenWithSmtpCredentials()
    {
        var (sender, _, options) = Sender("Production");
        options.CurrentValue.Enabled = true; options.CurrentValue.Host = "smtp.gmail.com"; options.CurrentValue.FromAddress = "a@example.test";
        Assert.False(sender.IsDemo); Assert.False(sender.IsConfigured);
        await Assert.ThrowsAsync<RegistrationEmailDeliveryException>(() => sender.SendAsync("b@example.test", "123456"));
    }
    [Fact] public async Task FixedDemoWorksWithoutOtpTableAndRequiresCorrectCode()
    {
        await using var f = await Fixture.Create(); var pending = await f.Start();
        Assert.Null(pending.Error); Assert.Equal("123456", f.Service.DemoCode);
        Assert.Empty(await f.Db.PortalUsers.ToListAsync());
        Assert.NotNull(await f.Service.Verify(pending.Id!.Value, f.Owner, "000000"));
        Assert.Null(await f.Service.Verify(pending.Id.Value, f.Owner, "123456"));
        var user = await f.Db.PortalUsers.SingleAsync(); Assert.False(user.IsApproved); Assert.False(user.IsActive); Assert.Equal("User", user.Role);
        var audit = await f.Db.TeamAuditEvents.SingleAsync(); Assert.Contains("\"EmailVerified\":false", audit.DetailsJson);
        Assert.NotNull(await f.Service.Verify(pending.Id.Value, f.Owner, "123456"));
        Assert.Single(await f.Db.PortalUsers.ToListAsync());
    }
    [Fact] public async Task DemoCannotBeReadOrVerifiedByAnotherSession()
    {
        await using var f = await Fixture.Create(); var pending = await f.Start(); var other = RegistrationOtpService.NewOwner();
        Assert.Null(await f.Service.Get(pending.Id!.Value, other));
        Assert.NotNull(await f.Service.Verify(pending.Id.Value, other, "123456"));
        Assert.NotNull(await f.Service.Resend(pending.Id.Value, other));
        await f.Service.Cancel(pending.Id.Value, other);
        Assert.NotNull(await f.Service.Get(pending.Id.Value, f.Owner));
    }
    [Fact] public async Task DemoLocksAfterFiveWrongCodesAndCannotRenewToBypassLock()
    {
        await using var f = await Fixture.Create(); var pending = await f.Start();
        for(int i=0;i<5;i++) Assert.NotNull(await f.Service.Verify(pending.Id!.Value, f.Owner, "bad"));
        Assert.NotNull(await f.Service.Verify(pending.Id!.Value, f.Owner, "123456"));
        Assert.NotNull(await f.Service.Resend(pending.Id.Value, f.Owner)); Assert.Empty(await f.Db.PortalUsers.ToListAsync());
    }
    [Fact] public async Task DemoExpiryRenewalAndCancellationNeedNoOtpTable()
    {
        await using var f = await Fixture.Create(); var pending = await f.Start();
        Assert.NotNull(await f.Service.Resend(pending.Id!.Value, f.Owner));
        f.Clock.Advance(TimeSpan.FromMinutes(6)); Assert.True((await f.Service.Get(pending.Id.Value, f.Owner))!.Expired);
        Assert.NotNull(await f.Service.Verify(pending.Id.Value, f.Owner, "123456"));
        Assert.Null(await f.Service.Resend(pending.Id.Value, f.Owner));
        await f.Service.Cancel(pending.Id.Value, f.Owner);
        Assert.NotNull(await f.Service.Verify(pending.Id.Value, f.Owner, "123456"));
        Assert.Empty(await f.Db.PortalUsers.ToListAsync());
    }
    [Fact] public async Task ParallelVerificationCreatesOnlyOneAccount()
    {
        await using var f = await Fixture.Create(); var pending = await f.Start();
        var results = await Task.WhenAll(f.Service.Verify(pending.Id!.Value, f.Owner, "123456"), f.Service.Verify(pending.Id.Value, f.Owner, "123456"));
        Assert.Single(results.Where(x => x == null)); Assert.Single(await f.Db.PortalUsers.ToListAsync());
    }
    [Fact] public async Task RestartedApplicationRequiresRegistrationToStartAgain()
    {
        await using var f = await Fixture.Create(); var pending = await f.Start();
        using var emptyCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var fresh = new DemoRegistrationService(f.Db, new DemoRegistrationStore(emptyCache), f.Clock);
        Assert.Null(await fresh.Get(pending.Id!.Value, f.Owner)); Assert.NotNull(await fresh.Verify(pending.Id.Value, f.Owner, "123456"));
    }
    private class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now += value;
    }
    private class Fixture : IAsyncDisposable
    {
        public QMSPortalDbContext Db { get; }
        public Clock Clock { get; } = new();
        public RegistrationOtpService Service { get; }
        public string Owner { get; } = RegistrationOtpService.NewOwner();
        private readonly SqliteConnection _connection;
        private readonly Microsoft.Extensions.Caching.Memory.MemoryCache _cache = new(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        private Fixture(SqliteConnection c, QMSPortalDbContext db)
        {
            _connection = c; Db = db; var sender = Sender("Development").Sender;
            Service = new(db, sender, Clock, new DemoRegistrationService(db, new DemoRegistrationStore(_cache), Clock));
        }
        public static async Task<Fixture> Create()
        {
            var c = new SqliteConnection("Data Source=:memory:"); await c.OpenAsync();
            c.CreateCollation("ascii_general_ci", (a,b) => string.Compare(a,b,StringComparison.OrdinalIgnoreCase));
            var db = new QMSPortalDbContext(new DbContextOptionsBuilder<QMSPortalDbContext>().UseSqlite(c).Options);
            await db.Database.EnsureCreatedAsync();
            // Reproduce the user's database: the OTP table is absent.
            await db.Database.ExecuteSqlRawAsync("DROP TABLE RegistrationChallenges");
            return new(c, db);
        }
        public Task<(Guid? Id, string? Error)> Start() => Service.Start("demo", "Demo Person", "demo@example.test", "123456789", "StrongPassword123!", Owner);
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); _cache.Dispose(); }
    }
    private class Options : IOptionsMonitor<RegistrationEmailOptions>
    {
        public RegistrationEmailOptions CurrentValue { get; } = new() { DemoMode = true };
        public RegistrationEmailOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<RegistrationEmailOptions, string?> listener) => null;
    }
    private class EnvironmentStub : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "QMSApplication";
        public string WebRootPath { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private class MemorySession : ISession
    {
        private readonly Dictionary<string,byte[]> _data = new(); public bool IsAvailable => true;
        public string Id { get; } = Guid.NewGuid().ToString(); public IEnumerable<string> Keys => _data.Keys;
        public void Clear() => _data.Clear(); public void Remove(string key) => _data.Remove(key);
        public void Set(string key, byte[] value) => _data[key] = value;
        public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? value) => _data.TryGetValue(key, out value);
        public Task LoadAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken token = default) => Task.CompletedTask;
    }
}
