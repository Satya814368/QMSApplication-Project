using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
namespace QMSApplication.Security;

public sealed class RegistrationOtpService
{
    public const string SessionChallenge = "RegistrationChallenge";
    public const string SessionOwner = "RegistrationOwner";
    private readonly QMSPortalDbContext _db;
    private readonly IRegistrationEmailSender _email;
    private readonly TimeProvider _clock;
    private readonly DemoRegistrationService? _demo;
    public RegistrationOtpService(QMSPortalDbContext db, IRegistrationEmailSender email, TimeProvider clock, DemoRegistrationService? demo = null)
    { _db = db; _email = email; _clock = clock; _demo = demo; }
    public bool CanSend => _email.IsConfigured;
    public bool IsDemo => _email.IsDemo;
    public string? DemoCode => IsDemo ? DemoRegistrationService.Code : null;
    public static string NewOwner() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string CodeDigest(string owner, Guid id, string code) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(owner), Encoding.UTF8.GetBytes(id + ":" + code)));
    private string Proof(string owner, Guid id, string code) => (IsDemo ? "!" + CodeDigest(owner, id, code)[1..] : CodeDigest(owner, id, code));
    private bool SameMode(RegistrationChallenge c) => c.CodeHash.StartsWith("!", StringComparison.Ordinal) == IsDemo;
    private static bool Equal(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    private static bool Owns(RegistrationChallenge c, string owner) => owner.Length >= 40 && Equal(c.OwnerHash, Digest(owner));
    private async Task<bool> Duplicate(RegistrationChallenge c) => await _db.PortalUsers.AnyAsync(u => u.Username.ToLower() == c.Username.ToLower() || (u.Email != null && u.Email.ToLower() == c.Email) || u.MobileNumber == c.MobileNumber);
    private async Task<bool> EmailLimit(string email)
    {
        var since = Now.AddHours(-1);
        return await _db.RegistrationChallenges.Where(c => c.Email == email && c.CreatedAtUtc >= since).SumAsync(c => (int?)c.SendCount) >= 5;
    }
    public async Task<(Guid? Id, string? Error)> Start(string username, string fullName, string email, string phone, string password, string owner)
    {
        if (!CanSend) return (null, "Registration email is not configured. Contact Admin to configure OTP delivery; no account has been created.");
        username = username.Trim(); fullName = fullName.Trim(); email = email.Trim().ToLowerInvariant(); phone = phone.Trim();
        if (!Regex.IsMatch(username, @"\A[A-Za-z0-9_.-]{3,100}\z") || fullName.Length is < 1 or > 200 ||
            email.Length > 200 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email) ||
            phone.Length is < 1 or > 20 || password.Length is < 12 or > 128 || owner.Length is < 40 or > 100)
            return (null, "Review the registration details and use a password between 12 and 128 characters.");
        if (IsDemo) return _demo == null ? (null, "Demo registration service is unavailable.") : await _demo.Start(username, fullName, email, phone, password, owner);
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var challenge = new RegistrationChallenge { Username = username, FullName = fullName, Email = email, MobileNumber = phone,
                OwnerHash = Digest(owner), CreatedAtUtc = Now, ExpiresAtUtc = Now.AddMinutes(5) };
            if (await Duplicate(challenge)) return (null, "Those details are already registered. Sign in or contact Admin.");
            if (await EmailLimit(email)) return (null, "Too many verification emails for this address. Wait an hour before trying again.");
            var recent = Now.AddSeconds(-60); var ownerHash = Digest(owner);
            if (await _db.RegistrationChallenges.AnyAsync(c => c.OwnerHash == ownerHash && c.LastSentAtUtc > recent))
                return (null, "Wait 60 seconds before requesting another code.");
            // Remove abandoned pending details after one day; completed rows are scrubbed immediately.
            var old = await _db.RegistrationChallenges.Where(c => c.CreatedAtUtc < Now.AddDays(-1)).ToListAsync();
            _db.RegistrationChallenges.RemoveRange(old);
            var previous = await _db.RegistrationChallenges.Where(c => c.OwnerHash == ownerHash && !c.IsConsumed).ToListAsync();
            foreach (var c in previous) { c.IsConsumed = true; c.CodeHash = ""; c.Version = Guid.NewGuid(); }
            challenge.PasswordHash = new PasswordHasher<PortalUser>().HashPassword(new PortalUser(), password);
            _db.RegistrationChallenges.Add(challenge);
            var error = await Send(challenge, owner);
            await _db.SaveChangesAsync(); await tx.CommitAsync();
            return error == null ? (challenge.Id, null) : (null, error);
        }
        catch (DbUpdateException) { _db.ChangeTracker.Clear(); return (null, "Registration changed concurrently. No account was created. Please try again."); }
    }
    private async Task<string?> Send(RegistrationChallenge challenge, string owner)
    {
        string code;
        do { code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture); }
        while (Equal(challenge.CodeHash, Proof(owner, challenge.Id, code)));
        challenge.CodeHash = Proof(owner, challenge.Id, code);
        challenge.SendCount++; challenge.LastSentAtUtc = Now; challenge.ExpiresAtUtc = Now.AddMinutes(5);
        challenge.IsSent = false; challenge.Version = Guid.NewGuid();
        try { await _email.SendAsync(challenge.Email, code); challenge.IsSent = true; return null; }
        catch (RegistrationEmailDeliveryException)
        {
            challenge.IsConsumed = true; challenge.CodeHash = "";
            return "The verification email could not be sent. No account was created. Check the address or contact Admin, then try again after 60 seconds.";
        }
    }
    public async Task<string?> Resend(Guid id, string owner)
    {
        if (IsDemo) return _demo == null ? "Demo registration service is unavailable." : await _demo.Resend(id, owner);
        if (!CanSend) return "OTP delivery is unavailable. Contact Admin.";
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var c = await _db.RegistrationChallenges.SingleOrDefaultAsync(x => x.Id == id);
            if (c == null || !Owns(c, owner) || !SameMode(c) || c.IsConsumed || c.AttemptCount >= 5 || c.CreatedAtUtc < Now.AddMinutes(-30))
                return "This registration has expired or is locked. Start again.";
            if (c.LastSentAtUtc > Now.AddSeconds(-60)) return "Wait 60 seconds before resending the code.";
            if (c.SendCount >= 3 || await EmailLimit(c.Email)) return "The email limit has been reached. Start again later.";
            var error = await Send(c, owner); await _db.SaveChangesAsync(); await tx.CommitAsync(); return error;
        }
        catch (DbUpdateException) { _db.ChangeTracker.Clear(); return "The verification changed. Refresh and try again."; }
    }
    public sealed record Status(string MaskedEmail, DateTime ExpiresAtUtc, int AttemptsRemaining, bool Expired);
    public async Task<Status?> Get(Guid id, string owner)
    {
        if (IsDemo) return _demo == null ? null : await _demo.Get(id, owner);
        var c = await _db.RegistrationChallenges.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (c == null || !Owns(c, owner) || !SameMode(c) || c.IsConsumed || !c.IsSent || c.AttemptCount >= 5 || c.CreatedAtUtc < Now.AddMinutes(-30)) return null;
        var at = c.Email.LastIndexOf('@');
        return new Status(c.Email[..1] + "***" + c.Email[at..], c.ExpiresAtUtc, 5 - c.AttemptCount, c.ExpiresAtUtc <= Now);
    }
    public async Task<string?> Verify(Guid id, string owner, string code)
    {
        if (IsDemo) return _demo == null ? "Demo registration service is unavailable." : await _demo.Verify(id, owner, code);
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var c = await _db.RegistrationChallenges.SingleOrDefaultAsync(x => x.Id == id);
            if (c == null || !Owns(c, owner) || !SameMode(c) || c.IsConsumed || !c.IsSent || c.AttemptCount >= 5 || c.ExpiresAtUtc <= Now)
                return "This code is invalid, expired or already used. Request another code or start again.";
            // Reserve an attempt with an atomic database update before testing the code.
            // The transaction retains the row lock, so parallel requests cannot all use the same remaining attempt.
            var now = Now; var ownerHash = Digest(owner); var nextVersion = Guid.NewGuid();
            var reserved = await _db.RegistrationChallenges.Where(x => x.Id == id && x.OwnerHash == ownerHash && !x.IsConsumed &&
                x.IsSent && x.AttemptCount < 5 && x.ExpiresAtUtc > now).ExecuteUpdateAsync(set => set
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1).SetProperty(x => x.Version, nextVersion));
            if (reserved != 1) return "This verification is expired, locked or already used. Start again.";
            await _db.Entry(c).ReloadAsync();
            if (!Regex.IsMatch(code ?? "", @"\A[0-9]{6}\z") || !Equal(c.CodeHash, Proof(owner, c.Id, code!)))
            {
                c.Version = Guid.NewGuid(); if (c.AttemptCount >= 5) { c.IsConsumed = true; c.CodeHash = ""; }
                await _db.SaveChangesAsync(); await tx.CommitAsync();
                return c.IsConsumed ? "Too many incorrect codes. Start registration again." : $"Incorrect code. {5 - c.AttemptCount} attempt(s) remaining.";
            }
            if (await Duplicate(c)) return "Those details are already registered. Sign in or contact Admin.";
            var user = new PortalUser { Username = c.Username, FullName = c.FullName, Email = c.Email, MobileNumber = c.MobileNumber,
                PasswordHash = c.PasswordHash, Role = "User", IsApproved = false, IsActive = false };
            _db.PortalUsers.Add(user);
            _db.TeamAuditEvents.Add(new TeamAuditEvent { TimestampUtc = Now, Username = user.Username, Action = "Registration pending approval", DetailsJson = IsDemo ? "{\"EmailVerified\":false,\"VerificationMode\":\"Demo\"}" : "{\"EmailVerified\":true}" });
            c.IsConsumed = true; c.IsSent = false; c.Version = Guid.NewGuid();
            c.Username = ""; c.FullName = ""; c.Email = ""; c.MobileNumber = ""; c.PasswordHash = ""; c.OwnerHash = ""; c.CodeHash = "";
            await _db.SaveChangesAsync(); await tx.CommitAsync(); return null;
        }
        catch (DbUpdateException) { _db.ChangeTracker.Clear(); return "The verification changed or was already used. Refresh before trying again."; }
    }
    public async Task Cancel(Guid id, string owner)
    {
        if (IsDemo) { if (_demo != null) await _demo.Cancel(id, owner); return; }
        var c = await _db.RegistrationChallenges.SingleOrDefaultAsync(x => x.Id == id);
        if (c == null || !Owns(c, owner)) return;
        c.IsConsumed = true; c.CodeHash = ""; c.PasswordHash = ""; c.Version = Guid.NewGuid();
        try { await _db.SaveChangesAsync(); } catch (DbUpdateException) { _db.ChangeTracker.Clear(); }
    }
}
