using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using QMSApplication.Data;

namespace QMSApplication.Security;
public sealed class AccessService
{
    private readonly QMSPortalDbContext _db;
    public AccessService(QMSPortalDbContext db) => _db = db;
    public static bool KnownRole(PortalUser user) => new[]
    {
        "admin",
        "manager",
        "team lead",
        "teamlead",
        "tl",
        "team lead (tl)",
        "user",
        "member",
        "guest"
    }.Contains(user.Role.Trim().ToLowerInvariant());
    public static string Role(PortalUser user) => TeamSetupService.Role(user.Role);
    public static bool CanCreate(PortalUser user) => user.IsActive && user.IsApproved && new[]
    {
        "Admin",
        "Manager",
        "Team Lead"
    }.Contains(Role(user));
    public async Task<PortalUser?> Current(HttpContext context)
    {
        if (context.Items.TryGetValue(typeof(AccessService), out var cached))
            return cached as PortalUser;
        var name = context.Session.GetString("Username");
        var stamp = context.Session.GetString("SecurityStamp");
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(stamp))
            return null;
        var user = await _db.PortalUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Username == name && x.IsActive && x.IsApproved);
        if (user == null || !KnownRole(user) || user.SecurityStamp.ToString() != stamp)
        {
            context.Session.Clear();
            return null;
        }

        context.Session.SetString("UserRole", Role(user));
        context.Items[typeof(AccessService)] = user;
        return user;
    }

    public IQueryable<Team> ManagedTeams(PortalUser actor)
    {
        var role = Role(actor);
        return _db.Teams.Where(t => actor.IsActive && actor.IsApproved && (role == "Admin" || (role == "Manager" && (_db.TeamManagers.Any(m => m.TeamId == t.Id && m.UserId == actor.Id) || (actor.ManagerDepartmentScope && actor.DepartmentId != null && t.DepartmentId == actor.DepartmentId))) || (role == "Team Lead" && t.LeadUserId == actor.Id)));
    }

    public IQueryable<MDEntity> Records(PortalUser actor)
    {
        var role = Role(actor);
        var records = _db.MDRecords.AsNoTracking();
        if (!actor.IsActive || !actor.IsApproved || !KnownRole(actor))
            return records.Where(x => false);
        if (role is "Admin" or "Guest")
            return records;
        if (role is "Manager" or "Team Lead")
        {
            var ids = ManagedTeams(actor).Select(t => t.Id);
            return records.Where(x => x.TeamId != null && ids.Contains(x.TeamId.Value));
        }

        return records.Where(x => x.AssignedTo == actor.Username || (actor.FullName != "" && x.AssignedTo == actor.FullName && _db.PortalUsers.Count(u => u.FullName == actor.FullName) == 1 && !_db.PortalUsers.Any(u => u.Username == actor.FullName)) || _db.DeliverableTasks.Any(t => t.DeliverableId == x.Id && (t.AssignedUserId == actor.Id || t.Targets.Any(y => y.AssignedUserId == actor.Id))));
    }

    public IQueryable<DeliverableTarget> Targets(PortalUser actor)
    {
        var ids = Records(actor).Select(x => x.Id);
        var query = _db.DeliverableTargets.Where(x => ids.Contains(x.Task.DeliverableId));
        return Role(actor) == "User" ? query.Where(x => x.AssignedUserId == actor.Id) : query;
    }

    public async Task<bool> Manage(PortalUser actor, MDEntity record) => CanCreate(actor) && (Role(actor) == "Admin" || (record.TeamId.HasValue && await ManagedTeams(actor).AnyAsync(t => t.Id == record.TeamId)));
    public async Task<bool> Delete(PortalUser actor, MDEntity record) => (Role(actor)is "Admin" or "Manager") && await Manage(actor, record);
    public IQueryable<PortalUser> Assignees(long teamId)
    {
        var members = _db.TeamMemberships.Where(x => x.TeamId == teamId).Select(x => x.UserId);
        var managers = _db.TeamManagers.Where(x => x.TeamId == teamId).Select(x => x.UserId);
        var lead = _db.Teams.Where(x => x.Id == teamId).Select(x => x.LeadUserId);
        return _db.PortalUsers.AsNoTracking().Where(x => x.IsActive && x.IsApproved && new[] { "user", "member", "manager", "team lead", "teamlead", "tl", "team lead (tl)", "admin" }.Contains(x.Role.Trim().ToLower()) && (members.Contains(x.Id) || managers.Contains(x.Id) || lead.Contains(x.Id)));
    }

    public async Task<bool> ValidAssignments(long teamId, string assignedTo, IEnumerable<DeliverableTask> tasks)
    {
        var users = await Assignees(teamId).Select(x => new { x.Id, x.Username, x.FullName }).ToListAsync();
        var ids = users.Select(x => x.Id).ToHashSet();
        return users.Any(x => x.Username == assignedTo || (x.FullName != "" && x.FullName == assignedTo)) && tasks.All(t => ids.Contains(t.AssignedUserId) && t.Targets.All(x => ids.Contains(x.AssignedUserId)));
    }
}
