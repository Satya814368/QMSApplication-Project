using QMSApplication.Models;
namespace QMSApplication.Security;
public static class RiskIssueAccess
{
    public static IQueryable<RiskIssue> Visible(QMSPortalDbContext db, PortalUser actor)
    {
        var q = db.RiskIssues.AsQueryable();
        if (!actor.IsActive || !actor.IsApproved || !AccessService.KnownRole(actor)) return q.Where(x => false);
        var role = AccessService.Role(actor);
        if (role == "Admin") return q;
        if (role is "Manager" or "Team Lead") { var ids = new AccessService(db).ManagedTeams(actor).Select(x => x.Id); return q.Where(x => ids.Contains(x.TeamId)); }
        if (role == "User") return q.Where(x => x.OwnerId == actor.Id || x.CreatedById == actor.Id);
        return q.Where(x => false); // Guest has no authorized risk scope by default.
    }
}
