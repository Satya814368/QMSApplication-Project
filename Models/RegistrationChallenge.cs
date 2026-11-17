using System.ComponentModel.DataAnnotations;
namespace QMSApplication.Models;

// Pending registration only. No PortalUser is created until the email code is verified.
public class RegistrationChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(100)] public string Username { get; set; } = "";
    [MaxLength(200)] public string FullName { get; set; } = "";
    [MaxLength(200)] public string Email { get; set; } = "";
    [MaxLength(20)] public string MobileNumber { get; set; } = "";
    [MaxLength(255)] public string PasswordHash { get; set; } = "";
    [MaxLength(64)] public string OwnerHash { get; set; } = "";
    [MaxLength(64)] public string CodeHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? LastSentAtUtc { get; set; }
    public int SendCount { get; set; }
    public int AttemptCount { get; set; }
    public bool IsSent { get; set; }
    public bool IsConsumed { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
