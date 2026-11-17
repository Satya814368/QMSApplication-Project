using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;

namespace QMSApplication.Security;
public sealed class AccountService
{
    private readonly QMSPortalDbContext _db;
    public static readonly string[] Roles =
    {
        "Admin",
        "Manager",
        "Team Lead",
        "User",
        "Guest"
    };
    public AccountService(QMSPortalDbContext db) => _db = db;
    public static string Digest(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private void Audit(string actor, string action, object value) => _db.TeamAuditEvents.Add(new TeamAuditEvent { TimestampUtc = DateTime.UtcNow, Username = actor, Action = action, DetailsJson = JsonSerializer.Serialize(value) });
    private async Task<PortalUser?> Admin(string name) => await _db.PortalUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Username == name && x.IsActive && x.IsApproved && x.Role.Trim().ToLower() == "admin");
    public async Task<string?> Change(string actorName, long id, Guid version, string role, bool approved, bool active, long? departmentId, bool departmentScope, string reason)
    {
        if (!Roles.Contains(role) || string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
            return "Choose a valid role and provide a reason (up to 500 characters).";
        if (active && !approved)
            return "Approve an account before activating it.";
        if (departmentScope && (role != "Manager" || !departmentId.HasValue))
            return "Department scope requires a Manager and a department.";
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if (await Admin(actorName) == null)
                return "Only an active Admin can manage users and roles.";
            if (departmentId.HasValue && !await _db.Departments.AnyAsync(x => x.Id == departmentId))
                return "Choose an existing department.";
            var user = await _db.PortalUsers.SingleOrDefaultAsync(x => x.Id == id);
            if (user == null || user.Version != version)
                return "This account changed. Refresh before saving.";
            if (AccessService.Role(user) == "Admin" && user.IsActive && user.IsApproved && (role != "Admin" || !active || !approved))
            {
                if (await _db.PortalUsers.CountAsync(x => x.IsActive && x.IsApproved && x.Role.Trim().ToLower() == "admin") <= 1)
                    return "Keep at least one active, approved Admin. Assign another Admin first.";
            }

            var before = new
            {
                user.Role,
                user.IsApproved,
                user.IsActive,
                user.DepartmentId,
                user.ManagerDepartmentScope
            };
            if (AccessService.Role(user) != role)
            {
                var leads = await _db.Teams.Where(x => x.LeadUserId == id).ToListAsync();
                foreach (var team in leads)
                {
                    team.LeadUserId = null;
                    team.Version = Guid.NewGuid();
                }

                _db.TeamManagers.RemoveRange(await _db.TeamManagers.Where(x => x.UserId == id).ToListAsync());
                _db.TeamMemberships.RemoveRange(await _db.TeamMemberships.Where(x => x.UserId == id).ToListAsync());
            }

            user.Role = role;
            user.IsApproved = approved;
            user.IsActive = active;
            user.DepartmentId = departmentId;
            user.ManagerDepartmentScope = departmentScope;
            user.SecurityStamp = Guid.NewGuid();
            user.Version = Guid.NewGuid();
            var grants = await _db.PasswordResetRequests.Where(x => x.UserId == id && !x.IsClosed).ToListAsync();
            foreach (var grant in grants)
            {
                grant.IsClosed = true;
                grant.Version = Guid.NewGuid();
            }

            Audit(actorName, "Account permissions changed", new { UserId = id, Before = before, After = new { role, approved, active, departmentId, departmentScope }, Reason = reason.Trim() });
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return null;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return "The account changed concurrently. No changes were saved; refresh and retry.";
        }
    }

    public async Task<string?> DeleteUser(string actorName, long id, Guid version, string confirmation, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
            return "Enter a deletion reason of up to 500 characters.";
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var actor = await Admin(actorName);
            if (actor == null) return "Only an active, approved Admin can delete users.";
            var user = await _db.PortalUsers.SingleOrDefaultAsync(x => x.Id == id);
            if (user == null || user.Version != version) return "This account changed or was removed. Refresh before deleting.";
            if (user.Id == actor.Id) return "You cannot delete your own signed-in account.";
            if (!string.Equals(confirmation?.Trim(), user.Username, StringComparison.Ordinal)) return "Type the exact username to confirm deletion.";
            if (AccessService.Role(user) == "Admin" && user.IsActive && user.IsApproved &&
                await _db.PortalUsers.CountAsync(x => x.IsActive && x.IsApproved && x.Role.Trim().ToLower() == "admin") <= 1)
                return "The last active, approved Admin cannot be deleted.";

            // Keep the identity behind assigned work, approvals and audit history.
            var hasLinks = await _db.TeamManagers.AnyAsync(x => x.UserId == id) ||
                await _db.TeamMemberships.AnyAsync(x => x.UserId == id) ||
                await _db.Teams.AnyAsync(x => x.LeadUserId == id || x.CreatedBy == user.Username) ||
                await _db.DeliverableTasks.AnyAsync(x => x.AssignedUserId == id || x.CreatedBy == user.Username) ||
                await _db.DeliverableTargets.AnyAsync(x => x.AssignedUserId == id || x.UpdatedBy == user.Username) ||
                await _db.MDRecords.AnyAsync(x => x.AssignedTo == user.Username ||
                    (user.FullName != "" && x.AssignedTo == user.FullName) || x.CreatedBy == user.Username || x.UpdatedBy == user.Username) ||
                await _db.MDAuditLogs.AnyAsync(x => x.Username == user.Username) ||
                await _db.TeamAuditEvents.AnyAsync(x => x.Username == user.Username && x.Action != "Registration pending approval") ||
                await _db.ImportBatches.AnyAsync(x => x.ImportedBy == user.Username) ||
                await _db.NotificationUserStatuses.AnyAsync(x => x.Username == user.Username);
            if (hasLinks) return "This user has team links, assigned work or recorded history. For an employee who left, uncheck Active, enter a reason and Save account. Reassign remaining work separately; history will be preserved.";

            _db.PasswordResetRequests.RemoveRange(await _db.PasswordResetRequests.Where(x => x.UserId == id).ToListAsync());
            Audit(actorName, "Account deleted", new { UserId = id, user.Username, user.FullName, user.Role, user.IsActive, Reason = reason.Trim() });
            _db.PortalUsers.Remove(user);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return null;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return "The account changed or gained a related record. Nothing was deleted. Refresh; deactivate the account if it has work history.";
        }
    }

    public async Task<string?> DeleteDepartment(string actorName, long id, string confirmation, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
            return "Enter a deletion reason of up to 500 characters.";
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if (await Admin(actorName) == null) return "Only an active, approved Admin can delete departments.";
            var department = await _db.Departments.SingleOrDefaultAsync(x => x.Id == id);
            if (department == null) return "Department was already removed. Refresh the page.";
            if (!string.Equals(confirmation?.Trim(), department.Name, StringComparison.Ordinal)) return "Type the exact department name to confirm deletion.";
            if (await _db.PortalUsers.AnyAsync(x => x.DepartmentId == id) || await _db.Teams.AnyAsync(x => x.DepartmentId == id))
                return "This department is assigned to users or teams, including inactive users. Move or clear their department assignments in Manage Users and My Team before deleting it.";
            Audit(actorName, "Department deleted", new { DepartmentId = id, department.Name, Reason = reason.Trim() });
            _db.Departments.Remove(department);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return null;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return "The department changed or is in use. Nothing was deleted. Refresh and check its user/team assignments.";
        }
    }

    public async Task RequestReset(string identifier)
    {
        identifier = identifier.Trim();
        if (identifier.Length > 200)
            return;
        var user = await _db.PortalUsers.AsNoTracking().FirstOrDefaultAsync(x => x.IsActive && x.IsApproved && (x.Username == identifier || x.Email == identifier));
        if (user == null)
            return;
        var since = DateTime.UtcNow.AddHours(-1);
        if (await _db.PasswordResetRequests.AnyAsync(x => x.UserId == user.Id && !x.IsClosed && x.RequestedAtUtc >= since))
            return;
        _db.PasswordResetRequests.Add(new PasswordResetRequest { UserId = user.Id, RequestedAtUtc = DateTime.UtcNow });
        await _db.SaveChangesAsync();
    }

    public async Task<(string? Token, string? Error)> IssueReset(string actor, long id)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if (await Admin(actor) == null)
                return (null, "Only Admin can issue password reset links.");
            var user = await _db.PortalUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsActive && x.IsApproved);
            if (user == null)
                return (null, "Choose an active, approved account.");
            var old = await _db.PasswordResetRequests.Where(x => x.UserId == id && !x.IsClosed).ToListAsync();
            foreach (var grant in old)
            {
                grant.IsClosed = true;
                grant.Version = Guid.NewGuid();
            }

            var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            _db.PasswordResetRequests.Add(new PasswordResetRequest { UserId = id, RequestedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15), TokenHash = Digest(token) });
            Audit(actor, "Password reset link issued", new { UserId = id, ExpiresInMinutes = 15 });
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return (token, null);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return (null, "Reset request changed. Refresh and retry.");
        }
    }

    public async Task<string?> Reset(string token, string password)
    {
        if (password.Length is < 12 or > 128 || string.IsNullOrEmpty(token) || token.Length > 100)
            return "Use a valid reset link and a password between 12 and 128 characters.";
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var hash = Digest(token);
            var now = DateTime.UtcNow;
            var grant = await _db.PasswordResetRequests.Include(x => x.User).SingleOrDefaultAsync(x => x.TokenHash == hash && !x.IsClosed && x.ExpiresAtUtc > now);
            if (grant == null || !grant.User.IsActive || !grant.User.IsApproved)
                return "This reset link is invalid, expired or already used. Contact Admin for a new link.";
            grant.User.PasswordHash = new PasswordHasher<PortalUser>().HashPassword(grant.User, password);
            grant.User.SecurityStamp = Guid.NewGuid();
            grant.User.Version = Guid.NewGuid();
            foreach (var pending in await _db.PasswordResetRequests.Where(x => x.UserId == grant.UserId && !x.IsClosed).ToListAsync())
            {
                pending.IsClosed = true;
                pending.Version = Guid.NewGuid();
            }

            Audit(grant.User.Username, "Password reset completed", new { grant.UserId });
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return null;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return "The reset link changed or was already used. Request a new one.";
        }
    }
}
