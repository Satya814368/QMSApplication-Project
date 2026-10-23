using System.ComponentModel.DataAnnotations;

namespace QMSApplication.Models;

// One language is one task. Targets inherit this assignee when created.
public class DeliverableTask
{
    public long Id { get; set; }
    public long DeliverableId { get; set; }
    public MDEntity Deliverable { get; set; } = null!;
    [Required, MaxLength(80)] public string Language { get; set; } = "";
    public long AssignedUserId { get; set; }
    public PortalUser AssignedUser { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    [Required, MaxLength(100)] public string CreatedBy { get; set; } = "";
    public List<DeliverableTarget> Targets { get; set; } = new();
}

public class DeliverableTarget
{
    public long Id { get; set; }
    public long TaskId { get; set; }
    public DeliverableTask Task { get; set; } = null!;
    [Required, MaxLength(40)] public string TargetNumber { get; set; } = "";
    public long AssignedUserId { get; set; }
    public PortalUser AssignedUser { get; set; } = null!;
    public bool IsCancelled { get; set; }
    [MaxLength(500)] public string? CancellationReason { get; set; }
    [MaxLength(200)] public string? GnApprovalReference { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    [Required, MaxLength(100)] public string UpdatedBy { get; set; } = "";
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public List<TargetProgressStep> Steps { get; set; } = new();
}

public class TargetProgressStep
{
    public long Id { get; set; }
    public long TargetId { get; set; }
    public DeliverableTarget Target { get; set; } = null!;
    public int StepNumber { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = "Not Started";
    public long MinutesSpent { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
