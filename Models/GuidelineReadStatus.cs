namespace QMSApplication.Models;

public class GuidelineReadStatus
{
    public long UserId { get; set; }
    public PortalUser User { get; set; } = null!;
    public long GuidelineId { get; set; }
    public Guideline Guideline { get; set; } = null!;
    public Guid SeenVersion { get; set; }
    public DateTime ReadAtUtc { get; set; }
}
