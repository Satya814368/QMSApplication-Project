using System.ComponentModel.DataAnnotations;

namespace QMSApplication.Models;

public class Team
{
    public long Id { get; set; }
    [MaxLength(120)] public string Name { get; set; } = "";
    [MaxLength(120)] public string NormalizedName { get; set; } = "";
    public long? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public long? LeadUserId { get; set; }
    public PortalUser? LeadUser { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; }
    [MaxLength(100)] public string CreatedBy { get; set; } = "";
}
public class TeamManager
{
    public long TeamId { get; set; }
    public Team Team { get; set; } = null!;
    public long UserId { get; set; }
    public PortalUser User { get; set; } = null!;
}
// A regular member belongs to at most one team; managers may manage many.
public class TeamMembership
{
    public long UserId { get; set; }
    public PortalUser User { get; set; } = null!;
    public long TeamId { get; set; }
    public Team Team { get; set; } = null!;
    public Guid Version { get; set; } = Guid.NewGuid();
}
public class TeamAuditEvent
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    [MaxLength(100)] public string Username { get; set; } = "";
    [MaxLength(80)] public string Action { get; set; } = "";
    public string DetailsJson { get; set; } = "";
}
