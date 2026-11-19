using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using QMSApplication.Security;
using Xunit;
namespace QMSApplication.TaskTests;
public class PasswordRecoveryOtpTests
{
    [Fact] public async Task CorrectOtpIsHashedSingleUseAndInvalidatesOldSessionsAndLinks()
    {
        await using var f=await Fixture.Create();var stamp=f.User.SecurityStamp;var oldHash=f.User.PasswordHash;var id=await f.Start();
        var c=await f.Db.PasswordRecoveryChallenges.SingleAsync();Assert.NotEqual(f.Mail.Code,c.CodeHash);Assert.NotEqual(f.Owner,c.OwnerHash);Assert.Equal(6,f.Mail.Code.Length);
        f.Db.PasswordResetRequests.Add(new PasswordResetRequest{UserId=f.User.Id,TokenHash="old",ExpiresAtUtc=f.Clock.GetUtcNow().UtcDateTime.AddHours(1)});await f.Db.SaveChangesAsync();
        Assert.Null(await f.Reset(id,f.Mail.Code));Assert.NotEqual(stamp,f.User.SecurityStamp);Assert.NotEqual(oldHash,f.User.PasswordHash);
        Assert.NotEqual(PasswordVerificationResult.Failed,new PasswordHasher<PortalUser>().VerifyHashedPassword(f.User,f.User.PasswordHash!,Fixture.NewPassword));
        Assert.True(c.IsConsumed);Assert.Equal("",c.CodeHash);Assert.True((await f.Db.PasswordResetRequests.SingleAsync()).IsClosed);Assert.NotNull(await f.Reset(id,f.Mail.Code));
        Assert.DoesNotContain(await f.Db.TeamAuditEvents.ToListAsync(),a=>a.DetailsJson.Contains(f.Mail.Code)||a.DetailsJson.Contains(Fixture.NewPassword));
    }
    [Fact] public async Task WrongOwnerExpiredAndReplacedCodesCannotResetPassword()
    {
        await using var f=await Fixture.Create();var id=await f.Start();var code=f.Mail.Code;
        Assert.NotNull(await f.Service.Reset(id,PasswordRecoveryOtpService.NewOwner(),code,Fixture.NewPassword,Fixture.NewPassword));
        f.Clock.Advance(TimeSpan.FromSeconds(61));var next=await f.Start();Assert.NotNull(await f.Reset(id,code));
        f.Clock.Advance(TimeSpan.FromMinutes(5));Assert.NotNull(await f.Reset(next,f.Mail.Code));
    }
    [Fact] public async Task FiveWrongCodesLockRequestEvenIfCorrectCodeIsLaterProvided()
    {
        await using var f=await Fixture.Create();var id=await f.Start();var wrong=f.Mail.Code=="000000"?"111111":"000000";
        for(int i=0;i<5;i++)Assert.NotNull(await f.Reset(id,wrong));Assert.NotNull(await f.Reset(id,f.Mail.Code));
        var c=await f.Db.PasswordRecoveryChallenges.SingleAsync();Assert.Equal(5,c.AttemptCount);Assert.True(c.IsConsumed);
    }
    [Fact] public async Task UnknownInactiveUnapprovedAndAmbiguousEmailsDoNotSendCodes()
    {
        await using var f=await Fixture.Create();await f.Service.Start("unknown@example.test",f.Owner);Assert.Equal(0,f.Mail.Sent);
        f.User.IsActive=false;await f.Db.SaveChangesAsync();await f.Start();Assert.Equal(0,f.Mail.Sent);
        f.User.IsActive=true;f.User.IsApproved=false;await f.Db.SaveChangesAsync();await f.Start();Assert.Equal(0,f.Mail.Sent);
        f.User.IsApproved=true;f.Db.PortalUsers.Add(new PortalUser{Username="duplicate",Email=f.User.Email});await f.Db.SaveChangesAsync();await f.Start();Assert.Equal(0,f.Mail.Sent);
        Assert.Empty(await f.Db.PasswordRecoveryChallenges.ToListAsync());
    }
    [Fact] public async Task CooldownAndHourlyLimitApplyAcrossBrowserOwners()
    {
        await using var f=await Fixture.Create();await f.Start();await f.Service.Start(f.User.Email!,PasswordRecoveryOtpService.NewOwner());Assert.Equal(1,f.Mail.Sent);
        f.Clock.Advance(TimeSpan.FromSeconds(61));await f.Start();f.Clock.Advance(TimeSpan.FromSeconds(61));await f.Start();
        f.Clock.Advance(TimeSpan.FromSeconds(61));await f.Start();Assert.Equal(3,f.Mail.Sent);
    }
    [Fact] public async Task PasswordMismatchFailureDeliveryAndAccountChangesPreservePassword()
    {
        await using var f=await Fixture.Create();var old=f.User.PasswordHash;var id=await f.Start();
        Assert.NotNull(await f.Service.Reset(id,f.Owner,f.Mail.Code,Fixture.NewPassword,"mismatch"));Assert.Equal(old,f.User.PasswordHash);
        f.User.SecurityStamp=Guid.NewGuid();await f.Db.SaveChangesAsync();Assert.NotNull(await f.Reset(id,f.Mail.Code));Assert.Equal(old,f.User.PasswordHash);
        f.Clock.Advance(TimeSpan.FromSeconds(61));f.Mail.Fail=true;id=await f.Start();Assert.NotNull(await f.Reset(id,f.Mail.Code));Assert.Equal(old,f.User.PasswordHash);
    }
    [Fact] public async Task DisabledEmailNeverFallsBackToFixedDemoRecoveryCode()
    {
        await using var f=await Fixture.Create();f.Mail.IsConfigured=false;
        Assert.False(f.Service.Ready);await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Start());Assert.NotNull(await f.Reset(Guid.NewGuid(),"123456"));Assert.Equal(0,f.Mail.Sent);
    }
    [Fact] public void RegistrationDemoSettingDoesNotDisableConfiguredRealRecoveryEmail()
    {
        var options=new EmailOptions();var environment=new TestEnvironment();
        var sender=new SmtpRegistrationEmailSender(options,Microsoft.Extensions.Logging.Abstractions.NullLogger<SmtpRegistrationEmailSender>.Instance,environment,new Microsoft.AspNetCore.Http.HttpContextAccessor());
        Assert.True(sender.IsDemo);Assert.True(sender.IsConfigured);Assert.True(sender.CanSendPasswordReset);
        options.CurrentValue.Enabled=false;Assert.True(sender.IsConfigured);Assert.False(sender.CanSendPasswordReset);
        options.CurrentValue.Enabled=true;environment.EnvironmentName="Production";Assert.False(sender.IsDemo);Assert.False(sender.IsConfigured);Assert.True(sender.CanSendPasswordReset);
    }
    private sealed class EmailOptions : Microsoft.Extensions.Options.IOptionsMonitor<RegistrationEmailOptions>
    {
        public RegistrationEmailOptions CurrentValue {get;}=new(){Enabled=true,DemoMode=true,Host="smtp.example.test",Port=587,FromAddress="sender@example.test",Username="sender@example.test",Password="placeholder"};
        public RegistrationEmailOptions Get(string? name)=>CurrentValue;
        public IDisposable? OnChange(Action<RegistrationEmailOptions,string?> listener)=>null;
    }
    private sealed class TestEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string EnvironmentName {get;set;}="Development";public string ApplicationName {get;set;}="Test";public string WebRootPath {get;set;}="";public string ContentRootPath {get;set;}="";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider {get;set;}=new Microsoft.Extensions.FileProviders.NullFileProvider();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider {get;set;}=new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
    internal sealed class Mail : IRegistrationEmailSender
    {
        public bool IsConfigured { get; set; }=true;public bool Fail;public string Code="";public int Sent;
        public Task SendAsync(string recipient,string code,CancellationToken ct=default)=>Task.CompletedTask;
        public Task SendPasswordResetAsync(string recipient,string code,CancellationToken ct=default){Code=code;if(Fail)throw new RegistrationEmailDeliveryException();Sent++;return Task.CompletedTask;}
    }
    internal sealed class Clock : TimeProvider
    {
        private DateTimeOffset now=new(2026,11,19,9,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow()=>now;
        public void Advance(TimeSpan by)=>now+=by;
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public const string NewPassword="NewStrongPassword123!";
        private readonly SqliteConnection connection;public QMSPortalDbContext Db;public PortalUser User=null!;public Mail Mail=new();public Clock Clock=new();
        public string Owner=PasswordRecoveryOtpService.NewOwner();public PasswordRecoveryOtpService Service=>new(Db,Mail,Clock);
        private Fixture(SqliteConnection c,QMSPortalDbContext db){connection=c;Db=db;}
        public static async Task<Fixture> Create(){var c=new SqliteConnection("Data Source=:memory:;Foreign Keys=True");await c.OpenAsync();c.CreateCollation("ascii_general_ci",(a,b)=>string.Compare(a,b,StringComparison.OrdinalIgnoreCase));var db=new QMSPortalDbContext(new DbContextOptionsBuilder<QMSPortalDbContext>().UseSqlite(c).Options);await db.Database.EnsureCreatedAsync();var f=new Fixture(c,db);f.User=new PortalUser{Username="person",FullName="Person",Email="person@example.test",Role="User",IsActive=true,IsApproved=true};f.User.PasswordHash=new PasswordHasher<PortalUser>().HashPassword(f.User,"OldStrongPassword123!");db.Add(f.User);await db.SaveChangesAsync();return f;}
        public Task<Guid> Start()=>Service.Start(User.Email!,Owner);
        public Task<string?> Reset(Guid id,string code)=>Service.Reset(id,Owner,code,NewPassword,NewPassword);
        public async ValueTask DisposeAsync(){await Db.DisposeAsync();await connection.DisposeAsync();}
    }
}
