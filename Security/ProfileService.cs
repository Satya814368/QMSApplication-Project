using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
namespace QMSApplication.Security;

// Only personal details and the signed-in account's team relationships are exposed here.
public sealed class ProfileService(QMSPortalDbContext db)
{
    public sealed record TeamInfo(string Name, string? Department, string? Lead, List<string> Managers);
    public sealed record Details(string Username, string FullName, string? Email, string? MobileNumber, string Role, string? Department, List<TeamInfo> Teams);
    public async Task<Details> Load(PortalUser actor)
    {
        var role = AccessService.Role(actor);
        var department = actor.DepartmentId == null ? null : await db.Departments.Where(d => d.Id == actor.DepartmentId).Select(d => d.Name).SingleOrDefaultAsync();
        var assigned = await db.Teams.AsNoTracking().Where(t => t.LeadUserId == actor.Id ||
            db.TeamMemberships.Any(m => m.TeamId == t.Id && m.UserId == actor.Id) ||
            db.TeamManagers.Any(m => m.TeamId == t.Id && m.UserId == actor.Id) ||
            (role == "Manager" && actor.ManagerDepartmentScope && actor.DepartmentId != null && t.DepartmentId == actor.DepartmentId))
            .OrderBy(t => t.Name).Select(t => new { t.Id, t.Name, Department = t.Department == null ? null : t.Department.Name, Lead = t.LeadUser == null ? null : t.LeadUser.FullName }).ToListAsync();
        var ids = assigned.Select(t => t.Id).ToList();
        var managers = await db.TeamManagers.AsNoTracking().Where(m => ids.Contains(m.TeamId)).Select(m => new { m.TeamId, m.User.FullName }).ToListAsync();
        return new(actor.Username, actor.FullName, actor.Email, actor.MobileNumber, role, department,
            assigned.Select(t => new TeamInfo(t.Name, t.Department, t.Lead, managers.Where(m => m.TeamId == t.Id).Select(m => m.FullName).ToList())).ToList());
    }
}
