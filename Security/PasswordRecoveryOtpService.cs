using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;

namespace QMSApplication.Security;
public sealed class PasswordRecoveryOtpService(QMSPortalDbContext db, IRegistrationEmailSender sender, TimeProvider clock)
{
    public const string SessionOwner = "PasswordRecoveryOwner", SessionChallenge = "PasswordRecoveryChallenge";
    public const string InvalidCode = "Invalid or expired verification code. Request another code.";
    public bool Ready => sender.CanSendPasswordReset;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public static string NewOwner() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string s) => AccountService.Digest(s);
    private static string CodeHash(string owner, Guid id, string code) => Hash(owner + ":" + id + ":" + code);
    private static bool Equal(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    public async Task<Guid> Start(string email, string owner)
    {
        if (!Ready) throw new InvalidOperationException("Password recovery email is not configured.");
        // Return the same browser flow for unknown, disabled and throttled accounts.
        var placeholder = Guid.NewGuid();
        if (string.IsNullOrWhiteSpace(email) || email.Length > 200 || owner.Length < 40) return placeholder;
        var normalized = email.Trim().ToLowerInvariant();
        PasswordRecoveryChallenge challenge;
        string address, code;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            try
            {
                // Ambiguous legacy email addresses must never choose an arbitrary account.
                var matches = await db.PortalUsers.Where(x => x.Email != null && x.Email.ToLower() == normalized).Take(2).ToListAsync();
                if (matches.Count != 1 || !matches[0].IsActive || !matches[0].IsApproved) return placeholder;
                var user = matches[0]; var now = Now;
                var sent = db.PasswordRecoveryChallenges.Where(x => x.UserId == user.Id);
                if (await sent.CountAsync(x => x.CreatedAtUtc >= now.AddHours(-1)) >= 3 ||
                    await sent.AnyAsync(x => x.LastSentAtUtc > now.AddSeconds(-60))) return placeholder;
                foreach (var previous in await sent.Where(x => !x.IsConsumed).ToListAsync())
                { previous.IsConsumed = true; previous.CodeHash = ""; previous.Version = Guid.NewGuid(); }
                code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
                challenge = new PasswordRecoveryChallenge { UserId = user.Id, AccountSecurityStamp = user.SecurityStamp,
                    OwnerHash = Hash(owner), CreatedAtUtc = now, LastSentAtUtc = now, ExpiresAtUtc = now.AddMinutes(5) };
                challenge.CodeHash = CodeHash(owner, challenge.Id, code);address = user.Email!;
                db.PasswordRecoveryChallenges.Add(challenge);
                await db.SaveChangesAsync(); await tx.CommitAsync();
            }
            catch (DbUpdateException) { db.ChangeTracker.Clear(); return placeholder; }
        }
        try { await sender.SendPasswordResetAsync(address, code); }
        catch (RegistrationEmailDeliveryException)
        {
            // Do not leak account existence or leave a usable undelivered code.
            challenge.IsConsumed = true; challenge.CodeHash = ""; challenge.Version = Guid.NewGuid();
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { db.ChangeTracker.Clear(); }
        }
        return challenge.Id;
    }

    public async Task<string?> Reset(Guid id, string owner, string code, string password, string confirmation)
    {
        if (!Ready) return "Email password recovery is unavailable. Contact the administrator.";
        if (password != confirmation || password.Length is < 12 or > 128)
            return "Passwords must match and contain 12 to 128 characters.";
        if (owner.Length < 40 || code.Length != 6 || !code.All(c => c is >= '0' and <= '9')) return InvalidCode;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var c = await db.PasswordRecoveryChallenges.SingleOrDefaultAsync(x => x.Id == id);var now = Now;
            if (c == null || c.IsConsumed || c.ExpiresAtUtc <= now || c.AttemptCount >= 5 || !Equal(c.OwnerHash, Hash(owner))) return InvalidCode;
            c.AttemptCount++;c.Version = Guid.NewGuid();
            if (!Equal(c.CodeHash, CodeHash(owner, id, code)))
            {
                if (c.AttemptCount >= 5) { c.IsConsumed = true; c.CodeHash = ""; }
                await db.SaveChangesAsync();await tx.CommitAsync();return InvalidCode;
            }
            var user = await db.PortalUsers.SingleOrDefaultAsync(x => x.Id == c.UserId && x.IsActive && x.IsApproved);
            if (user == null || user.SecurityStamp != c.AccountSecurityStamp) return InvalidCode;
            user.PasswordHash = new PasswordHasher<PortalUser>().HashPassword(user, password);
            user.SecurityStamp = Guid.NewGuid();user.Version = Guid.NewGuid();
            foreach (var pending in await db.PasswordRecoveryChallenges.Where(x => x.UserId == user.Id && !x.IsConsumed).ToListAsync())
            { pending.IsConsumed = true;pending.CodeHash = "";pending.Version = Guid.NewGuid(); }
            foreach (var grant in await db.PasswordResetRequests.Where(x => x.UserId == user.Id && !x.IsClosed).ToListAsync())
            { grant.IsClosed = true;grant.Version = Guid.NewGuid(); }
            db.TeamAuditEvents.Add(new TeamAuditEvent { TimestampUtc = now,Username = user.Username,
                Action = "Password reset completed by email OTP",DetailsJson = JsonSerializer.Serialize(new { UserId = user.Id }) });
            await db.SaveChangesAsync();await tx.CommitAsync();return null;
        }
        catch (DbUpdateException) { db.ChangeTracker.Clear();return "This recovery request changed or was already used. Request another code."; }
    }
}
