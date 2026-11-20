using System.ComponentModel.DataAnnotations;
namespace QMSApplication.Models;
public class TimeEntry
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public PortalUser User { get; set; } = null!;
    public long? TargetId { get; set; }
    public DeliverableTarget? Target { get; set; }
    [Required, MaxLength(24)] public string Kind { get; set; } = "Task";
    [MaxLength(120)] public string Description { get; set; } = "";
    public DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public int? WorkMinutes { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = "Draft";
    public long? CreatedById { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? ReviewedUtc { get; set; }
    public long? ReviewedById { get; set; }
    [MaxLength(500)] public string? ReviewComment { get; set; }
}
public class TimeEntryAudit
{
    public long Id { get; set; }
    public long EntryId { get; set; }
    public long ActorId { get; set; }
    [MaxLength(40)] public string Action { get; set; } = "";
    [MaxLength(1000)] public string Details { get; set; } = "";
    public DateTime AtUtc { get; set; }
}
