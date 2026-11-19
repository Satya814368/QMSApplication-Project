using System.Data;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
namespace QMSApplication.Security;

public sealed class GuidelineService(QMSPortalDbContext db)
{
    public static readonly string[] Roles = { "Admin", "Manager", "Team Lead", "User", "Guest" };
    public static int RoleMask(PortalUser actor)
    {
        if (!AccessService.KnownRole(actor)) return 0;
        var index = Array.IndexOf(Roles, AccessService.Role(actor));
        return index < 0 ? 0 : 1 << index;
    }
    private static bool Active(PortalUser actor) => actor.IsActive && actor.IsApproved && RoleMask(actor) != 0;
    public static bool CanManage(PortalUser actor) => Active(actor) && AccessService.Role(actor) is "Admin" or "Manager";
    public static string Audience(int mask) => string.Join(", ", Roles.Where((_, i) => (mask & (1 << i)) != 0));
    // Legacy HTML is converted to text and then Razor-encoded. New content is plain text.
    public static string DisplayContent(Guideline g)
    {
        if (g.ContentIsPlainText) return g.Content;
        var text = Regex.Replace(g.Content, @"<\s*(br\s*/?|/p|/div|/li|/h[1-6])\s*>", "\n", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        return WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]*>", "", RegexOptions.None, TimeSpan.FromSeconds(1))).Trim();
    }
    public async Task<bool> HasUnread(PortalUser actor)
    {
        if (!Active(actor)) return false;
        var mask = RoleMask(actor);
        return await db.Guidelines.AsNoTracking().AnyAsync(g => g.IsActive && (g.TargetRoles & mask) != 0 &&
            !db.GuidelineReadStatuses.Any(r => r.UserId == actor.Id && r.GuidelineId == g.Id && r.SeenVersion == g.Version));
    }
    // The exact versions loaded for display are marked read; a concurrent later edit remains unread.
    public async Task<List<Guideline>> Open(PortalUser actor)
    {
        if (!Active(actor)) return new();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var mask = RoleMask(actor); var manage = CanManage(actor);
        var guidelines = await db.Guidelines.AsNoTracking().Where(g => g.IsActive && (manage || (g.TargetRoles & mask) != 0))
            .OrderBy(g => g.DisplayOrder).ThenBy(g => g.Id).ToListAsync();
        var targeted = guidelines.Where(g => (g.TargetRoles & mask) != 0).ToList();
        var ids = targeted.Select(g => g.Id).ToList();
        var receipts = await db.GuidelineReadStatuses.Where(r => r.UserId == actor.Id && ids.Contains(r.GuidelineId)).ToListAsync();
        foreach (var g in targeted)
        {
            var receipt = receipts.FirstOrDefault(r => r.GuidelineId == g.Id);
            if (receipt == null) { receipt = new GuidelineReadStatus { UserId = actor.Id, GuidelineId = g.Id }; db.GuidelineReadStatuses.Add(receipt); }
            receipt.SeenVersion = g.Version; receipt.ReadAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(); await tx.CommitAsync(); return guidelines;
    }
    public async Task<string?> Save(PortalUser actor, long? id, Guid version, string title, string content, int order, int targetRoles)
    {
        if (!CanManage(actor)) return "Only Admin and Manager can change guidelines.";
        title = (title ?? "").Trim(); content = (content ?? "").Trim();
        if (title.Length is < 1 or > 200 || content.Length is < 1 or > 20000 || order is < 0 or > 10000 || targetRoles <= 0 || (targetRoles & ~31) != 0)
            return "Enter a title, guidance text, valid display order and at least one target role.";
        var g = id.HasValue ? await db.Guidelines.SingleOrDefaultAsync(x => x.Id == id && x.IsActive) : null;
        if (id.HasValue && (g == null || g.Version != version)) return "This guideline changed or was deleted. Reload before editing.";
        var before = g == null ? null : JsonSerializer.Serialize(g);
        if (g == null)
        {
            g = new Guideline { GuidelineCode = "GL-" + Guid.NewGuid().ToString("N")[..12], CreatedAtUtc = DateTime.UtcNow };
            db.Guidelines.Add(g);
        }
        g.Title = title; g.Content = content; g.DisplayOrder = order; g.TargetRoles = targetRoles;
        g.ContentIsPlainText = true; g.Version = Guid.NewGuid(); g.UpdatedAtUtc = DateTime.UtcNow; g.UpdatedBy = actor.Username;
        db.TeamAuditEvents.Add(new TeamAuditEvent { TimestampUtc = g.UpdatedAtUtc, Username = actor.Username,
            Action = id.HasValue ? "Guideline edited" : "Guideline added", DetailsJson = JsonSerializer.Serialize(new { g.GuidelineCode, Before = before, After = g }) });
        try { await db.SaveChangesAsync(); return null; }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return "Another person changed this guideline. Reload before editing."; }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return "Unable to save the guideline. Refresh and try again."; }
    }
    public async Task<string?> Delete(PortalUser actor, long id, Guid version)
    {
        if (!CanManage(actor)) return "Only Admin and Manager can delete guidelines.";
        var g = await db.Guidelines.SingleOrDefaultAsync(x => x.Id == id && x.IsActive);
        if (g == null || g.Version != version) return "This guideline changed or was deleted. Reload before deleting.";
        g.IsActive = false; g.Version = Guid.NewGuid(); g.UpdatedAtUtc = DateTime.UtcNow; g.UpdatedBy = actor.Username;
        db.TeamAuditEvents.Add(new TeamAuditEvent { TimestampUtc = g.UpdatedAtUtc, Username = actor.Username, Action = "Guideline deleted",
            DetailsJson = JsonSerializer.Serialize(new { g.Id, g.GuidelineCode, g.Title, g.TargetRoles }) });
        try { await db.SaveChangesAsync(); return null; }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return "Another person changed this guideline. Reload before deleting."; }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return "Unable to delete the guideline. Refresh and try again."; }
    }
}
