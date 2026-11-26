using System.ComponentModel.DataAnnotations;
namespace QMSApplication.Models;
public class RiskIssue
{
    public long Id { get; set; }
    [MaxLength(10)] public string Kind { get; set; } = "Risk";
    [MaxLength(200)] public string Title { get; set; } = "";
    [MaxLength(50)] public string Category { get; set; } = "Schedule";
    [MaxLength(4000)] public string Description { get; set; } = "";
    [MaxLength(4000)] public string ActionPlan { get; set; } = "";
    public int Probability { get; set; }
    public int Impact { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "Open";
    public long TeamId { get; set; }
    public long OwnerId { get; set; }
    public long CreatedById { get; set; }
    public long? DeliverableId { get; set; }
    public DateOnly DueDate { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public int Score => Probability * Impact;
    public string Severity => Score >= 15 ? "Critical" : Score >= 10 ? "High" : Score >= 5 ? "Medium" : "Low";
}
public class RiskIssueAudit
{
    public long Id { get; set; }
    public long RiskIssueId { get; set; }
    [MaxLength(100)] public string Actor { get; set; } = "";
    public DateTime AtUtc { get; set; }
    public string BeforeJson { get; set; } = "";
    public string AfterJson { get; set; } = "";
}
