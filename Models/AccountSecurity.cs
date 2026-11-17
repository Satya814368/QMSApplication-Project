using System.ComponentModel.DataAnnotations;

namespace QMSApplication.Models;
public class Department
{
    public long Id { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = "";
    [MaxLength(120)]
    public string NormalizedName { get; set; } = "";
}

public class PasswordResetRequest
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public PortalUser User { get; set; } = null !;
    public DateTime RequestedAtUtc { get; set; }

    [MaxLength(64)]
    public string? TokenHash { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    public bool IsClosed { get; set; }

    public Guid Version { get; set; } = Guid.NewGuid();
}
