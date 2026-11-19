using System.ComponentModel.DataAnnotations;
namespace QMSApplication.Models;
public sealed class PasswordRecoveryChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long UserId { get; set; }
    public Guid AccountSecurityStamp { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    [MaxLength(64)] public string OwnerHash { get; set; } = "";
    [MaxLength(64)] public string CodeHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime LastSentAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public bool IsConsumed { get; set; }
}
