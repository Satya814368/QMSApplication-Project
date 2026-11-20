using System.ComponentModel.DataAnnotations;
namespace QMSApplication.Models;
public class UserPreference
{
    public long Id { get; set; }
    public long UserId { get; set; }
    [MaxLength(16)] public string Theme { get; set; } = "Light";
    [MaxLength(16)] public string Language { get; set; } = "English";
    [MaxLength(24)] public string DateFormat { get; set; } = "dd-MM-yyyy";
    public bool TaskAlerts { get; set; } = true;
    public bool ApprovalAlerts { get; set; } = true;
    public bool EmailAlerts { get; set; } = true;
    public PortalUser User { get; set; } = null!;
}
public class WorkspaceSetting
{
    [Key, MaxLength(80)] public string Key { get; set; } = "";
    [MaxLength(500)] public string Value { get; set; } = "";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public long UpdatedById { get; set; }
}
public class SettingsAuditEvent
{
    public long Id { get; set; }
    public long ActorId { get; set; }
    [MaxLength(80)] public string SettingKey { get; set; } = "";
    [MaxLength(500)] public string? PreviousValue { get; set; }
    [MaxLength(500)] public string? NewValue { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}
