using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;

namespace QMSApplication.Security;
public sealed class PasswordRecoveryOtpService
{
    private readonly QMSPortalDbContext _db;
    private readonly IRegistrationEmailSender _sender;
    public PasswordRecoveryOtpService(QMSPortalDbContext db, IRegistrationEmailSender sender)
    { _db = db; _sender = sender; }
    public static string NewOwner() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    private static string CodeHash(string owner, Guid id, string code) => Hash(owner + ":" + id + ":" + code);
    private static bool Equal(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    public bool Ready => _sender.IsConfigured && !_sender.IsDemo;

    public async Task<Guid> Start(string email, string owner)
    {
        if (!Ready) throw new InvalidOperationException("Real SMTP email delivery is not configured.");
        // Random placeholder ensures the same browser flow for unknown accounts.
        var result = Guid.NewGuid();
        if (string.IsNullOrWhiteSpace(email) || email.Length > 200 || owner.Length < 40) return result;
        var normalized = email.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;
        var user = await _db.PortalUsers.AsNoTracking().FirstOrDefaultAsync(x =>
            x.IsActive && x.IsApproved && x.Email != null && x.Email.ToLower() == normalized);
        if (user == null || string.IsNullOrWhiteSpace(user.Email)) return result;
        // Limit sends per account; don't disclose throttling or account existence.
        var sentLastHour = await _db.PasswordRecoveryChallenges.CountAsync(x => x.UserId == user.Id && x.CreatedAtUtc >= now.AddHours(-1));
        if (sentLastHour >= 3) return result;
        var last = await _db.PasswordRecoveryChallenges.Where(x => x.UserId == user.Id)
            .OrderByDescending(x => x.LastSentAtUtc).Select(x => (DateTime?)x.LastSentAtUtc).FirstOrDefaultAsync();
        if (last.HasValue && last.Value > now.AddSeconds(-60)) return result;
        var previous = await _db.PasswordRecoveryChallenges.Where(x => x.UserId == user.Id && !x.IsConsumed).ToListAsync();
        foreach (var item in previous) { item.IsConsumed = true; item.CodeHash = ""; }
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var challenge = new PasswordRecoveryChallenge {
            Id = Guid.NewGuid(), UserId = user.Id, OwnerHash = Hash(owner),
            CreatedAtUtc = now, LastSentAtUtc = now, ExpiresAtUtc = now.AddMinutes(5),
            CodeHash = CodeHash(owner, Guid.Empty, code)
        };
        _db.PasswordRecoveryChallenges.Add(challenge);
        await _db.SaveChangesAsync();
        try { await _sender.SendPasswordResetAsync(user.Email, code); }
        catch (RegistrationEmailDeliveryException)
        {
            challenge.IsConsumed = true; challenge.CodeHash = "";
            await _db.SaveChangesAsync();
            // Do not reveal account existence via error messages.
        }
        return challenge.Id;
    }

    public async Task<string?> Reset(Guid id, string owner, string code, string password, string confirmation)
    {
        if (password != confirmation || password.Length is < 12 or > 128)
            return "Passwords must match and contain 12 to 128 characters.";
        if (owner.Length < 40 || code.Length != 6 || !code.All(c => c >= '0' && c <= '9'))
            return "Invalid or expired verification code. Request another code.";
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var challenge = await _db.PasswordRecoveryChallenges.SingleOrDefaultAsync(x => x.Id == id);
        var now = DateTime.UtcNow;
        if (challenge == null || challenge.IsConsumed || challenge.ExpiresAtUtc <= now || challenge.AttemptCount >= 5 || !Equal(challenge.OwnerHash, Hash(owner)))
            return "Invalid or expired verification code. Request another code.";
        challenge.AttemptCount++;
        if (!Equal(challenge.CodeHash, CodeHash(owner, Guid.Empty, code)))
        {
            if (challenge.AttemptCount >= 5) { challenge.IsConsumed = true; challenge.CodeHash = ""; }
            await _db.SaveChangesAsync(); await tx.CommitAsync();
            return "Invalid or expired verification code. Request another code.";
        }
        var user = await _db.PortalUsers.SingleOrDefaultAsync(x => x.Id == challenge.UserId && x.IsActive && x.IsApproved);
        if (user == null) return "Account is not eligible for password recovery.";
        user.PasswordHash = new PasswordHasher<PortalUser>().HashPassword(user, password);
        user.SecurityStamp = Guid.NewGuid(); user.Version = Guid.NewGuid();
        challenge.IsConsumed = true; challenge.CodeHash = "";
        var grants = await _db.PasswordResetRequests.Where(x => x.UserId == user.Id && !x.IsClosed).ToListAsync();
        foreach (var grant in grants) { grant.IsClosed = true; grant.Version = Guid.NewGuid(); }
        var other = await _db.PasswordRecoveryChallenges.Where(x => x.UserId == user.Id && !x.IsConsumed && x.Id != id).ToListAsync();
        foreach (var c in other) { c.IsConsumed = true; c.CodeHash = ""; }
        _db.TeamAuditEvents.Add(new TeamAuditEvent { TimestampUtc = now, Username = user.Username, Action = "Password reset completed by email OTP", DetailsJson = JsonSerializer.Serialize(new { UserId = user.Id }) });
        await _db.SaveChangesAsync(); await tx.CommitAsync();
        return null;
    }
}
