using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using QMSApplication.Security;
using Xunit;
namespace QMSApplication.TaskTests;

public sealed class TestRegistrationEmailSender : IRegistrationEmailSender
{
    public bool IsConfigured { get; set; } = true;
    public bool Fail { get; set; }
    public List<(string Email, string Code)> Messages { get; } = new();
    public Task SendAsync(string recipient, string code, CancellationToken cancellationToken = default)
    {
        if (Fail) throw new RegistrationEmailDeliveryException();
        Messages.Add((recipient, code)); return Task.CompletedTask;
    }
}
public class RegistrationOtpTests
{
    [Fact] public async Task SendingCodeCreatesNoUserAndDoesNotPersistThePasswordOrPlainOtp()
    {
        await using var f = await Fixture.Create(); var pending = await f.Start();
        Assert.NotNull(pending.Id); Assert.Null(pending.Error); Assert.Empty(await f.Db.PortalUsers.ToListAsync());
        var row = await f.Db.RegistrationChallenges.SingleAsync(); var sent = Assert.Single(f.Email.Messages);
        Assert.Matches("^[0-9]{6}$", sent.Code); Assert.NotEqual(sent.Code, row.CodeHash); Assert.NotEqual("StrongPassword123!", row.PasswordHash);
        Assert.Equal("person@example.test", sent.Email); Assert.NotEqual(f.Owner, row.OwnerHash);
        var status = await f.Service.Get(row.Id, f.Owner); Assert.Equal("p***@example.test", status!.MaskedEmail);
    }
    [Fact] public async Task OnlyCorrectCodeCreatesOnePendingUserAndTheCodeCannotBeReused()
    {
        await using var f = await Fixture.Create(); var pending = await f.Start(); var code = f.Email.Messages.Single().Code;
        Assert.Null(await f.Service.Verify(pending.Id!.Value, f.Owner, code));
        var user = await f.Db.PortalUsers.SingleAsync(); Assert.Equal("person", user.Username); Assert.Equal("User", user.Role); Assert.False(user.IsActive); Assert.False(user.IsApproved);
        Assert.NotNull(await f.Service.Verify(pending.Id.Value, f.Owner, code)); Assert.Single(await f.Db.PortalUsers.ToListAsync());
        var row = await f.Db.RegistrationChallenges.SingleAsync(); Assert.True(row.IsConsumed); Assert.Empty(row.PasswordHash); Assert.Empty(row.CodeHash); Assert.Empty(row.Email);
        Assert.Contains(await f.Db.TeamAuditEvents.ToListAsync(), a => a.Action == "Registration pending approval" && a.DetailsJson.Contains("EmailVerified"));
    }
    [Fact] public async Task WrongOrMalformedCodesShareAFiveAttemptLimitAndCannotBeResentAfterLock()
    {
        await using var f = await Fixture.Create(); var pending = await f.Start(); var code = f.Email.Messages.Single().Code;
        for (var i = 0; i < 5; i++) Assert.NotNull(await f.Service.Verify(pending.Id!.Value, f.Owner, "bad"));
        Assert.NotNull(await f.Service.Verify(pending.Id!.Value, f.Owner, code)); Assert.NotNull(await f.Service.Resend(pending.Id.Value, f.Owner));
        Assert.Empty(await f.Db.PortalUsers.ToListAsync()); Assert.Equal(5, (await f.Db.RegistrationChallenges.SingleAsync()).AttemptCount);
    }
    [Fact] public async Task AnotherBrowserCannotReadVerifyOrResendTheChallenge()
    {
        await using var f = await Fixture.Create(); var pending = await f.Start(); var otherOwner = RegistrationOtpService.NewOwner();
        Assert.Null(await f.Service.Get(pending.Id!.Value, otherOwner));
        Assert.NotNull(await f.Service.Verify(pending.Id.Value, otherOwner, f.Email.Messages.Single().Code));
        Assert.NotNull(await f.Service.Resend(pending.Id.Value, otherOwner)); Assert.Single(f.Email.Messages);
        Assert.Equal(0, (await f.Db.RegistrationChallenges.SingleAsync()).AttemptCount);
    }
    [Fact] public async Task ExpiredCodeFailsAndResendReplacesTheOldCodeWithoutResettingAttempts()
    {
        await using var f = await Fixture.Create(); var pending = await f.Start(); var old = f.Email.Messages.Single().Code;
        Assert.NotNull(await f.Service.Verify(pending.Id!.Value, f.Owner, "bad"));
        f.Clock.Advance(TimeSpan.FromMinutes(6)); Assert.NotNull(await f.Service.Verify(pending.Id.Value, f.Owner, old));
        Assert.Null(await f.Service.Resend(pending.Id.Value, f.Owner)); var latest = f.Email.Messages.Last().Code;
        Assert.NotEqual(old, latest); Assert.NotNull(await f.Service.Verify(pending.Id.Value, f.Owner, old));
        Assert.Equal(2, (await f.Db.RegistrationChallenges.SingleAsync()).AttemptCount);
        Assert.Null(await f.Service.Verify(pending.Id.Value, f.Owner, latest));
    }
    [Fact] public async Task ResendHasCooldownAndThreeSendMaximum()
    {
        await using var f = await Fixture.Create(); var pending = await f.Start();
        Assert.NotNull(await f.Service.Resend(pending.Id!.Value, f.Owner)); Assert.Single(f.Email.Messages);
        f.Clock.Advance(TimeSpan.FromSeconds(61)); Assert.Null(await f.Service.Resend(pending.Id.Value, f.Owner));
        f.Clock.Advance(TimeSpan.FromSeconds(61)); Assert.Null(await f.Service.Resend(pending.Id.Value, f.Owner));
        f.Clock.Advance(TimeSpan.FromSeconds(61)); Assert.NotNull(await f.Service.Resend(pending.Id.Value, f.Owner)); Assert.Equal(3, f.Email.Messages.Count);
    }
    [Fact] public async Task UnconfiguredOrFailedEmailDeliveryNeverRegistersAUser()
    {
        await using var f = await Fixture.Create(); f.Email.IsConfigured = false;
        Assert.Null((await f.Start()).Id); Assert.Empty(await f.Db.PortalUsers.ToListAsync()); Assert.Empty(await f.Db.RegistrationChallenges.ToListAsync());
        f.Email.IsConfigured = true; f.Email.Fail = true; Assert.Null((await f.Start()).Id);
        Assert.Empty(await f.Db.PortalUsers.ToListAsync()); Assert.True((await f.Db.RegistrationChallenges.SingleAsync()).IsConsumed);
    }
    [Fact] public async Task RepeatedStartCannotBypassCooldownOrEmailQuota()
    {
        await using var f = await Fixture.Create(); Assert.NotNull((await f.Start()).Id); Assert.Null((await f.Start()).Id);
        for (int i = 0; i < 4; i++) { f.Clock.Advance(TimeSpan.FromSeconds(61)); Assert.NotNull((await f.Start()).Id); }
        f.Clock.Advance(TimeSpan.FromSeconds(61)); Assert.Null((await f.Start()).Id); Assert.Equal(5, f.Email.Messages.Count);
    }
    [Fact] public async Task StartingAgainInvalidatesEarlierChallengeAndCancellationCannotBeVerified()
    {
        await using var f = await Fixture.Create(); var first = await f.Start(); var firstCode = f.Email.Messages.Last().Code;
        f.Clock.Advance(TimeSpan.FromSeconds(61)); var second = await f.Start(); Assert.NotNull(second.Id);
        Assert.NotNull(await f.Service.Verify(first.Id!.Value, f.Owner, firstCode));
        await f.Service.Cancel(second.Id!.Value, f.Owner); Assert.NotNull(await f.Service.Verify(second.Id.Value, f.Owner, f.Email.Messages.Last().Code));
        Assert.Empty(await f.Db.PortalUsers.ToListAsync());
    }
    [Fact] public async Task DuplicateAccountCreatedWhileWaitingCannotBeRegisteredAgain()
    {
        await using var f = await Fixture.Create(); var pending = await f.Start();
        f.Db.PortalUsers.Add(new PortalUser { Username = "person", Email = "person@example.test", FullName = "Existing Person" }); await f.Db.SaveChangesAsync();
        Assert.NotNull(await f.Service.Verify(pending.Id!.Value, f.Owner, f.Email.Messages.Last().Code)); Assert.Single(await f.Db.PortalUsers.ToListAsync());
    }
    [Fact] public async Task StartRejectsInvalidDetailsAndDoesNotSendAnEmail()
    {
        await using var f = await Fixture.Create(); Assert.Null((await f.Service.Start("bad user", "Name", "bademail", "123", "short", f.Owner)).Id);
        Assert.Empty(f.Email.Messages); Assert.Empty(await f.Db.PortalUsers.ToListAsync());
    }
    [Fact] public async Task CorrectCodeWorksOnTheFifthAttemptAndFurtherRequestsCannotConsumeItAgain()
    {
        await using var f = await Fixture.Create(); var pending = await f.Start();
        for (int i = 0; i < 4; i++) Assert.NotNull(await f.Service.Verify(pending.Id!.Value, f.Owner, "bad"));
        Assert.Null(await f.Service.Verify(pending.Id!.Value, f.Owner, f.Email.Messages.Last().Code));
        Assert.Equal(5, (await f.Db.RegistrationChallenges.SingleAsync()).AttemptCount);
        Assert.Single(await f.Db.PortalUsers.ToListAsync());
    }
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan time) => _now += time;
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public QMSPortalDbContext Db { get; }
        public TestRegistrationEmailSender Email { get; } = new();
        public TestClock Clock { get; } = new();
        public string Owner { get; } = RegistrationOtpService.NewOwner();
        public RegistrationOtpService Service => new(Db, Email, Clock);
        private Fixture(SqliteConnection c, QMSPortalDbContext db) { _connection = c; Db = db; }
        public static async Task<Fixture> Create()
        {
            var c = new SqliteConnection("Data Source=:memory:;Foreign Keys=True"); await c.OpenAsync(); c.CreateCollation("ascii_general_ci", (a,b) => string.Compare(a,b,StringComparison.OrdinalIgnoreCase));
            var db = new QMSPortalDbContext(new DbContextOptionsBuilder<QMSPortalDbContext>().UseSqlite(c).Options); await db.Database.EnsureCreatedAsync(); return new Fixture(c, db);
        }
        public Task<(Guid? Id, string? Error)> Start() => Service.Start("person", "Test Person", "person@example.test", "123456789", "StrongPassword123!", Owner);
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
    }
}
