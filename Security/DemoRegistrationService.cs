using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QMSApplication.Models;
namespace QMSApplication.Security;

// Temporary demo registrations live in server memory. The browser session holds only their ID and owner secret.
public sealed class DemoRegistrationStore(IMemoryCache cache)
{
    internal IMemoryCache Cache { get; } = cache;
    internal SemaphoreSlim Gate { get; } = new(1, 1);
}

public sealed class DemoRegistrationService(QMSPortalDbContext db, DemoRegistrationStore store, TimeProvider clock)
{
    public const string Code = "123456";
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private string Key(Guid id) => "DemoRegistration:" + id;
    private static string OwnerKey(string owner) => "DemoRegistrationOwner:" + owner;
    private sealed class Pending
    {
        public required RegistrationChallenge Details { get; init; }
        public required string Owner { get; init; }
    }
    private Pending? Find(Guid id, string owner)
        => store.Cache.TryGetValue<Pending>(Key(id), out var value) && value?.Owner == owner ? value : null;
    private Task<bool> Duplicate(RegistrationChallenge c) => db.PortalUsers.AnyAsync(u =>
        u.Username.ToLower() == c.Username.ToLower() || (u.Email != null && u.Email.ToLower() == c.Email) || u.MobileNumber == c.MobileNumber);
    private void Remove(Pending p)
    {
        store.Cache.Remove(Key(p.Details.Id));
        p.Details.PasswordHash = "";
    }
    public async Task<(Guid? Id, string? Error)> Start(string username, string fullName, string email, string phone, string password, string owner)
    {
        await store.Gate.WaitAsync();
        try
        {
            var c = new RegistrationChallenge { Username = username, FullName = fullName, Email = email, MobileNumber = phone,
                CreatedAtUtc = Now, ExpiresAtUtc = Now.AddMinutes(5), LastSentAtUtc = Now, SendCount = 1 };
            if (await Duplicate(c)) return (null, "Those details are already registered. Sign in or contact Admin.");
            if (store.Cache.TryGetValue<Guid>(OwnerKey(owner), out var oldId) && Find(oldId, owner) is { } previous)
            {
                if (previous.Details.LastSentAtUtc > Now.AddSeconds(-60)) return (null, "Wait 60 seconds before starting again.");
                Remove(previous);
            }
            c.PasswordHash = new PasswordHasher<PortalUser>().HashPassword(new PortalUser(), password);
            var pending = new Pending { Details = c, Owner = owner };
            store.Cache.Set(Key(c.Id), pending, TimeSpan.FromMinutes(30));
            store.Cache.Set(OwnerKey(owner), c.Id, TimeSpan.FromMinutes(30));
            return (c.Id, null);
        }
        finally { store.Gate.Release(); }
    }
    public async Task<RegistrationOtpService.Status?> Get(Guid id, string owner)
    {
        await store.Gate.WaitAsync();
        try
        {
            var p = Find(id, owner); if (p == null || p.Details.AttemptCount >= 5) return null;
            var c = p.Details; var at = c.Email.LastIndexOf('@');
            return new(c.Email[..1] + "***" + c.Email[at..], c.ExpiresAtUtc, 5 - c.AttemptCount, c.ExpiresAtUtc <= Now);
        }
        finally { store.Gate.Release(); }
    }
    public async Task<string?> Resend(Guid id, string owner)
    {
        await store.Gate.WaitAsync();
        try
        {
            var p = Find(id, owner); if (p == null || p.Details.AttemptCount >= 5) return "This demo registration has expired or is locked. Start again.";
            var c = p.Details;
            if (c.LastSentAtUtc > Now.AddSeconds(-60)) return "Wait 60 seconds before renewing the demo code.";
            if (c.SendCount >= 3) return "The demo code renewal limit has been reached. Start again.";
            c.LastSentAtUtc = Now; c.ExpiresAtUtc = Now.AddMinutes(5); c.SendCount++;
            return null;
        }
        finally { store.Gate.Release(); }
    }
    public async Task<string?> Verify(Guid id, string owner, string code)
    {
        await store.Gate.WaitAsync();
        try
        {
            var p = Find(id, owner);
            if (p == null || p.Details.AttemptCount >= 5 || p.Details.ExpiresAtUtc <= Now)
                return "This demo registration is expired, locked or already used. Start again.";
            var c = p.Details; c.AttemptCount++;
            if (code != Code)
            {
                if (c.AttemptCount >= 5) { Remove(p); return "Too many incorrect codes. Start registration again."; }
                return $"Incorrect code. {5 - c.AttemptCount} attempt(s) remaining.";
            }
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                if (await Duplicate(c)) { Remove(p); return "Those details are already registered. Sign in or contact Admin."; }
                var user = new PortalUser { Username = c.Username, FullName = c.FullName, Email = c.Email, MobileNumber = c.MobileNumber,
                    PasswordHash = c.PasswordHash, Role = "User", IsActive = false, IsApproved = false };
                db.PortalUsers.Add(user);
                db.TeamAuditEvents.Add(new TeamAuditEvent { TimestampUtc = Now, Username = c.Username,
                    Action = "Registration pending approval", DetailsJson = "{\"EmailVerified\":false,\"VerificationMode\":\"Demo\"}" });
                await db.SaveChangesAsync(); await tx.CommitAsync(); Remove(p); return null;
            }
            catch (DbUpdateException) { db.ChangeTracker.Clear(); return "Registration could not be saved. Refresh and try again."; }
        }
        finally { store.Gate.Release(); }
    }
    public async Task Cancel(Guid id, string owner)
    {
        await store.Gate.WaitAsync();
        try { if (Find(id, owner) is { } p) Remove(p); }
        finally { store.Gate.Release(); }
    }
}
