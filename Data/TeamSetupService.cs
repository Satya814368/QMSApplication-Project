using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;

namespace QMSApplication.Data;

public sealed class TeamSetupService
{
    private readonly QMSPortalDbContext _db;
    public TeamSetupService(QMSPortalDbContext db) => _db = db;
    public static string Role(string? role) => role?.Trim().ToLowerInvariant() switch
    {
        "admin" => "Admin", "manager" => "Manager",
        "tl" or "teamlead" or "team lead" or "team lead (tl)" => "Team Lead",
        "member" or "user" => "User", _ => "Guest"
    };
    public IQueryable<Team> ManagedTeams(PortalUser actor)
    {
        return new QMSApplication.Security.AccessService(_db).ManagedTeams(actor);
    }

    private async Task<string?> Run(string username, Func<PortalUser, Task<string?>> change)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var actor = await _db.PortalUsers.AsNoTracking().SingleOrDefaultAsync(x => x.IsActive && x.IsApproved && x.Username == username);
            if (actor == null) return "Your account is inactive or unavailable. Sign in again.";
            var error = await change(actor);
            if (error != null) { _db.ChangeTracker.Clear(); return error; }
            await _db.SaveChangesAsync(); await transaction.CommitAsync(); return null;
        }
        catch (DbUpdateConcurrencyException)
        { _db.ChangeTracker.Clear(); return "The team, member or deliverable changed. Reload before trying again."; }
        catch (DbUpdateException)
        { _db.ChangeTracker.Clear(); return "The change was not saved. Check for a duplicate team name or a lead already assigned elsewhere, then refresh."; }
    }
    private void Audit(PortalUser actor, string action, object details) => _db.TeamAuditEvents.Add(new TeamAuditEvent
    { TimestampUtc = DateTime.UtcNow, Username = actor.Username, Action = action, DetailsJson = JsonSerializer.Serialize(details) });
    private static string? AdminOnly(PortalUser actor) => Role(actor.Role) == "Admin" ? null : "Only Admin can change team setup or deliverable ownership.";
    public Task<string?> Create(string actor, string name, long? departmentId = null) => Run(actor, async user =>
    {
        if (Role(user.Role) is not ("Admin" or "Manager")) return "Only Admin and Manager can create teams.";
        if (Role(user.Role) == "Manager") departmentId = user.DepartmentId;
        if (departmentId.HasValue && !await _db.Departments.AnyAsync(x => x.Id == departmentId)) return "Choose an existing department.";
        name = name.Trim(); if (name.Length is < 1 or > 120) return "Enter a team name of 1–120 characters.";
        var normalized = name.ToUpperInvariant();
        if (await _db.Teams.AnyAsync(x => x.NormalizedName == normalized)) return "A team with that name already exists.";
        var team = new Team { Name = name, NormalizedName = normalized, DepartmentId = departmentId, CreatedAtUtc = DateTime.UtcNow, CreatedBy = user.Username };
        _db.Teams.Add(team);
        if (Role(user.Role) == "Manager") _db.TeamManagers.Add(new TeamManager { Team = team, UserId = user.Id });
        Audit(user, "Team created", new { Name = name }); return null;
    });
    public Task<string?> SetLead(string actor, long teamId, long? leadId, Guid version) => Run(actor, async user =>
    {
        if (Role(user.Role) is not ("Admin" or "Manager")) return "Only Admin and the team's Manager can change its lead.";
        var team = await ManagedTeams(user).SingleOrDefaultAsync(x => x.Id == teamId);
        if (team == null || team.Version != version) return "Team changed or not found. Refresh the page.";
        if (leadId.HasValue)
        {
            var lead = await _db.PortalUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == leadId && x.IsActive && x.IsApproved);
            if (lead == null || Role(lead.Role) != "Team Lead") return "Select an active account with the Team Lead role.";
            if (await _db.Teams.AnyAsync(x => x.Id != teamId && x.LeadUserId == leadId)) return "That Team Lead already leads another team. Remove that lead assignment first.";
        }
        var before = team.LeadUserId; team.LeadUserId = leadId; team.Version = Guid.NewGuid();
        Audit(user, "Team lead changed", new { TeamId = teamId, Before = before, After = leadId }); return null;
    });
    public Task<string?> SetDepartment(string actor, long teamId, long? departmentId, Guid version) => Run(actor, async user =>
    {
        if (AdminOnly(user) is string denied) return denied;
        if (departmentId.HasValue && !await _db.Departments.AnyAsync(x => x.Id == departmentId)) return "Choose an existing department.";
        var team = await _db.Teams.SingleOrDefaultAsync(x => x.Id == teamId);
        if (team == null || team.Version != version) return "Team changed or not found. Refresh the page.";
        var before = team.DepartmentId; team.DepartmentId = departmentId; team.Version = Guid.NewGuid();
        Audit(user, "Team department changed", new { TeamId = teamId, Before = before, After = departmentId }); return null;
    });
    public Task<string?> SetManager(string actor, long teamId, long managerId, bool remove, Guid version) => Run(actor, async user =>
    {
        if (AdminOnly(user) is string denied) return denied;
        var team = await _db.Teams.SingleOrDefaultAsync(x => x.Id == teamId);
        if (team == null || team.Version != version) return "Team changed or not found. Refresh the page.";
        var manager = await _db.PortalUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == managerId && x.IsActive && x.IsApproved);
        if (!remove && (manager == null || Role(manager.Role) != "Manager")) return "Select an active account with the Manager role.";
        var link = await _db.TeamManagers.SingleOrDefaultAsync(x => x.TeamId == teamId && x.UserId == managerId);
        if (remove && link != null) _db.TeamManagers.Remove(link);
        if (!remove && link == null) _db.TeamManagers.Add(new TeamManager { TeamId = teamId, UserId = managerId });
        team.Version = Guid.NewGuid();
        Audit(user, remove ? "Manager removed" : "Manager assigned", new { TeamId = teamId, ManagerId = managerId }); return null;
    });
    public Task<string?> MoveMember(string actor, long memberId, long destinationId, long? expectedSourceId, string? reason) => Run(actor, async user =>
    {
        var destination = await ManagedTeams(user).SingleOrDefaultAsync(x => x.Id == destinationId);
        if (destination == null) return "You can move members only into a team you manage or lead.";
        var member = await _db.PortalUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == memberId && x.IsActive && x.IsApproved);
        if (member == null || Role(member.Role) != "User") return "Choose an active User account. Leadership assignments are managed separately by Admin.";
        var link = await _db.TeamMemberships.SingleOrDefaultAsync(x => x.UserId == memberId);
        if (link?.TeamId != expectedSourceId) return "This member's team changed. Refresh and review before transferring.";
        if (link?.TeamId == destinationId) return "This member is already in that team.";
        if (reason?.Length > 500 || (link != null && string.IsNullOrWhiteSpace(reason))) return "A transfer requires a reason of up to 500 characters.";
        var before = link?.TeamId;
        if (link == null) _db.TeamMemberships.Add(new TeamMembership { UserId = memberId, TeamId = destinationId });
        else { link.TeamId = destinationId; link.Version = Guid.NewGuid(); }
        destination.Version = Guid.NewGuid();
        Audit(user, before == null ? "Member assigned" : "Member transferred", new { MemberId = memberId, Before = before, After = destinationId, Reason = reason?.Trim() }); return null;
    });
    public Task<string?> RemoveMember(string actor, long memberId, long expectedTeamId, string? reason) => Run(actor, async user =>
    {
        var source = await ManagedTeams(user).SingleOrDefaultAsync(x => x.Id == expectedTeamId);
        if (source == null) return "You cannot remove members from that team.";
        var link = await _db.TeamMemberships.SingleOrDefaultAsync(x => x.UserId == memberId && x.TeamId == expectedTeamId);
        if (link == null) return "Membership changed. Refresh the page.";
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500) return "Enter a removal reason of up to 500 characters.";
        _db.TeamMemberships.Remove(link); source.Version = Guid.NewGuid();
        Audit(user, "Member unassigned", new { MemberId = memberId, Before = expectedTeamId, Reason = reason.Trim() }); return null;
    });
    public Task<string?> MapDeliverable(string actor, long recordId, long? teamId, Guid version) => Run(actor, async user =>
    {
        if (AdminOnly(user) is string denied) return denied;
        if (teamId.HasValue && !await _db.Teams.AnyAsync(x => x.Id == teamId)) return "Choose an existing team.";
        var record = await _db.MDRecords.SingleOrDefaultAsync(x => x.Id == recordId);
        if (record == null || record.ConcurrencyToken != version) return "Deliverable changed or not found. Refresh the page.";
        var before = record.TeamId; record.TeamId = teamId; record.ConcurrencyToken = Guid.NewGuid();
        record.UpdatedAtUtc = DateTime.UtcNow; record.UpdatedBy = user.Username;
        Audit(user, "Deliverable team changed", new { record.RecordID, Before = before, After = teamId }); return null;
    });
}
