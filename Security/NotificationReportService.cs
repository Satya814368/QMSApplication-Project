using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
namespace QMSApplication.Security;

public sealed class NotificationReportService(QMSPortalDbContext db)
{
    public static readonly string[] TargetRoles = { "Admin", "Manager", "Team Lead", "User", "Guest" };
    public static bool CanTrack(PortalUser actor) => actor.IsActive && actor.IsApproved && AccessService.Role(actor) is "Admin" or "Manager" or "Team Lead";
    public static bool TargetMatches(string? target, string role)
        => string.IsNullOrWhiteSpace(target) || TargetRoles.Contains(QMSApplication.Data.TeamSetupService.Role(target)) &&
            QMSApplication.Data.TeamSetupService.Role(target) == QMSApplication.Data.TeamSetupService.Role(role) &&
            new[] { "admin", "manager", "team lead", "teamlead", "tl", "team lead (tl)", "user", "member", "guest" }.Contains(target.Trim().ToLowerInvariant());
    public static string[] Aliases(string? role) => QMSApplication.Data.TeamSetupService.Role(role) switch
    {
        "User" => new[] { "user", "member" },
        "Team Lead" => new[] { "team lead", "teamlead", "tl", "team lead (tl)" },
        "Admin" => new[] { "admin" }, "Manager" => new[] { "manager" }, _ => new[] { "guest" }
    };
    public static IQueryable<Notification> Relevant(QMSPortalDbContext context, string? role)
    {
        var aliases = Aliases(role);
        return context.Notifications.AsNoTracking().Where(n => n.IsPublished && (n.TargetRole == null || n.TargetRole.Trim() == "" || aliases.Contains(n.TargetRole.Trim().ToLower())));
    }
    public sealed class Report
    {
        public bool Forbidden { get; set; }
        public string? Error { get; set; }
        public List<Department> Departments { get; set; } = new();
        public List<Team> Teams { get; set; } = new();
        public List<Notification> Notifications { get; set; } = new();
        public Notification? Selected { get; set; }
        public List<Recipient> Recipients { get; set; } = new();
        public int Acknowledged => Recipients.Count(x => x.IsAcknowledged);
        public int Pending => Recipients.Count - Acknowledged;
    }
    public sealed record Recipient(string Username, string FullName, string Role, string Department, string Team, bool IsRead, bool IsAcknowledged, DateTime? AcknowledgedAtUtc);
    public async Task<Report> Build(PortalUser actor, long? notificationId = null, long? departmentId = null, long? teamId = null)
    {
        var result = new Report();
        if (!CanTrack(actor)) { result.Forbidden = true; return result; }
        var admin = AccessService.Role(actor) == "Admin";
        if (!admin && departmentId.HasValue) { result.Forbidden = true; return result; }
        result.Departments = admin ? await db.Departments.AsNoTracking().OrderBy(x => x.Name).ToListAsync() : new();
        if (departmentId.HasValue && departmentId != 0 && !result.Departments.Any(x => x.Id == departmentId))
        { result.Error = "Choose an existing department."; return result; }
        var teamsQuery = new AccessService(db).ManagedTeams(actor).AsNoTracking();
        // Admin's department filter concerns people's Admin-assigned departments, including unassigned members.
        result.Teams = await teamsQuery.OrderBy(x => x.Name).ToListAsync();
        var teamIds = result.Teams.Select(x => x.Id).ToList();
        if (teamId.HasValue && !teamIds.Contains(teamId.Value)) { result.Forbidden = true; return result; }
        var scopeIds = teamId.HasValue ? new List<long> { teamId.Value } : teamIds;
        var membershipIds = db.TeamMemberships.Where(x => scopeIds.Contains(x.TeamId)).Select(x => x.UserId);
        var managerIds = db.TeamManagers.Where(x => scopeIds.Contains(x.TeamId)).Select(x => x.UserId);
        var leadIds = db.Teams.Where(x => scopeIds.Contains(x.Id)).Select(x => x.LeadUserId);
        IQueryable<PortalUser> query = db.PortalUsers.AsNoTracking().Include(x => x.Department).Where(x => x.IsActive && x.IsApproved);
        if (!admin || teamId.HasValue) query = query.Where(x => membershipIds.Contains(x.Id) || managerIds.Contains(x.Id) || leadIds.Contains(x.Id));
        if (departmentId.HasValue) query = departmentId == 0 ? query.Where(x => x.DepartmentId == null) : query.Where(x => x.DepartmentId == departmentId);
        var users = (await query.OrderBy(x => x.FullName).ThenBy(x => x.Username).ToListAsync()).Where(AccessService.KnownRole).ToList();
        var published = await db.Notifications.AsNoTracking().Where(x => x.IsPublished).OrderByDescending(x => x.NotificationDateUtc).ThenByDescending(x => x.Id).ToListAsync();
        result.Notifications = admin ? published : published.Where(n => users.Any(u => TargetMatches(n.TargetRole,u.Role))).ToList();
        result.Selected = notificationId.HasValue ? result.Notifications.FirstOrDefault(n => n.Id == notificationId) : result.Notifications.FirstOrDefault();
        if (notificationId.HasValue && result.Selected == null) { result.Forbidden = true; return result; }
        if (result.Selected == null) return result;
        users = users.Where(u => TargetMatches(result.Selected.TargetRole,u.Role)).ToList();
        var usernames = users.Select(x => x.Username).ToList();
        var statuses = await db.NotificationUserStatuses.AsNoTracking().Where(x => x.NotificationId == result.Selected.Id && usernames.Contains(x.Username)).ToListAsync();
        var userIds = users.Select(x => x.Id).ToList();
        var memberships = await db.TeamMemberships.AsNoTracking().Where(x => userIds.Contains(x.UserId) && scopeIds.Contains(x.TeamId)).ToListAsync();
        var managers = await db.TeamManagers.AsNoTracking().Where(x => userIds.Contains(x.UserId) && scopeIds.Contains(x.TeamId)).ToListAsync();
        foreach (var user in users)
        {
            var read = statuses.Where(x => string.Equals(x.Username,user.Username,StringComparison.OrdinalIgnoreCase)).ToList();
            var acknowledged = read.Any(x => x.IsAcknowledged);
            var teams = result.Teams.Where(t => t.LeadUserId == user.Id || memberships.Any(m => m.UserId == user.Id && m.TeamId == t.Id) || managers.Any(m => m.UserId == user.Id && m.TeamId == t.Id));
            result.Recipients.Add(new(user.Username,user.FullName,AccessService.Role(user),user.Department?.Name ?? "No department",
                string.Join(", ",teams.Select(t => t.Name).DefaultIfEmpty("No team")),read.Any(x => x.IsRead),acknowledged,
                read.Where(x => x.IsAcknowledged).Select(x => x.AcknowledgedAtUtc).Max()));
        }
        return result;
    }
}
