using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using QMSApplication.Security;
using System.Text.Json;
namespace QMSApplication.Pages;
public class RisksModel(QMSPortalDbContext db) : PageModel
{
    public List<RiskIssue> Rows { get; set; } = new();
    public List<RiskIssueAudit> History { get; set; } = new();
    public List<Team> Teams { get; set; } = new();
    public List<PortalUser> Owners { get; set; } = new();
    public RiskIssue? Edit { get; set; }
    public bool CanManage { get; set; }
    public long ActorId { get; set; }
    public bool CanReport { get; set; }
    public string[] Categories { get; } = ["Schedule", "Quality", "Resource", "Technical", "Operational", "Other"];
    public async Task<IActionResult> OnGetAsync(long? id)
    {
        var access = new AccessService(db); var actor = await access.Current(HttpContext);
        if (actor == null) return RedirectToPage("/Login");
        ActorId = actor.Id; CanManage = AccessService.CanCreate(actor); CanReport = CanManage || AccessService.Role(actor) == "User";
        var q = RiskIssueAccess.Visible(db, actor).AsNoTracking();
        Rows = await q.OrderByDescending(x => x.Id).ToListAsync();
        if (id.HasValue) { Edit = await q.SingleOrDefaultAsync(x => x.Id == id); if (Edit == null) return NotFound(); if (CanManage) History = await db.RiskIssueAudits.AsNoTracking().Where(x => x.RiskIssueId == id).OrderByDescending(x => x.Id).ToListAsync(); }
        var teamIds = db.TeamMemberships.Where(x => x.UserId == actor.Id).Select(x => x.TeamId);
        Teams = CanManage ? await access.ManagedTeams(actor).ToListAsync() : await db.Teams.Where(x => teamIds.Contains(x.Id)).ToListAsync();
        if (CanManage) { foreach(var t in Teams) Owners.AddRange(await access.Assignees(t.Id).ToListAsync()); Owners = Owners.DistinctBy(x => x.Id).OrderBy(x => x.FullName).ToList(); }
        else Owners.Add(actor);
        return Page();
    }
    public async Task<IActionResult> OnPostSaveAsync(long id, Guid version, string? kind, string? title, string? category, string? description, string? actionPlan, int probability, int impact, string? status, long teamId, long ownerId, long? deliverableId, DateOnly dueDate)
    {
        var access = new AccessService(db); var actor = await access.Current(HttpContext);
        if (actor == null) return RedirectToPage("/Login");
        actionPlan ??= "";
        var manager = AccessService.CanCreate(actor); var user = AccessService.Role(actor) == "User";
        if (!manager && !user) return StatusCode(403);
        var row = id == 0 ? new RiskIssue { CreatedById = actor.Id } : await RiskIssueAccess.Visible(db, actor).SingleOrDefaultAsync(x => x.Id == id);
        if (row == null) return NotFound();
        if (id != 0 && row.Version != version) return ConflictResult();
        var before = id == 0 ? "" : JsonSerializer.Serialize(row);
        if (user && id != 0)
        {
            if (row.OwnerId != actor.Id || row.Status == "Closed" || actionPlan == null || actionPlan.Length > 4000 || status is not ("Open" or "In Progress" or "Resolved")) return BadRequest("Invalid action update.");
            row.ActionPlan = actionPlan; row.Status = status;
        }
        else
        {
            if (!ModelState.IsValid || kind is not ("Risk" or "Issue") || string.IsNullOrWhiteSpace(title) || title.Length > 200 || !Categories.Contains(category) || string.IsNullOrWhiteSpace(description) || description.Length > 4000 || actionPlan == null || actionPlan.Length > 4000 || dueDate == default || status is not ("Open" or "In Progress" or "Resolved" or "Closed") || (kind == "Risk" && (probability < 1 || probability > 5 || impact < 1 || impact > 5))) return BadRequest("Invalid register fields.");
            if (user && (kind != "Issue" || ownerId != actor.Id || status != "Open" || !await db.TeamMemberships.AnyAsync(x => x.UserId == actor.Id && x.TeamId == teamId))) return StatusCode(403);
            if (manager && (!await access.ManagedTeams(actor).AnyAsync(x => x.Id == teamId) || !await access.Assignees(teamId).AnyAsync(x => x.Id == ownerId))) return StatusCode(403);
            if (deliverableId.HasValue && !await access.Records(actor).AnyAsync(x => x.Id == deliverableId && x.TeamId == teamId)) return BadRequest("Deliverable must be visible and belong to selected team.");
            if ((status is "Resolved" or "Closed") && string.IsNullOrWhiteSpace(actionPlan)) return BadRequest("Resolution action required.");
            row.Kind = kind; row.Title = title.Trim(); row.Category = category!; row.Description = description.Trim(); row.ActionPlan = actionPlan; row.Probability = kind == "Risk" ? probability : 0; row.Impact = kind == "Risk" ? impact : 0; row.Status = status; row.TeamId = teamId; row.OwnerId = ownerId; row.DeliverableId = deliverableId; row.DueDate = dueDate;
        }
        row.UpdatedUtc = DateTime.UtcNow; row.Version = Guid.NewGuid();
        await using var tx = await db.Database.BeginTransactionAsync();
        try { if (id == 0) db.RiskIssues.Add(row); await db.SaveChangesAsync(); db.RiskIssueAudits.Add(new RiskIssueAudit { RiskIssueId = row.Id, Actor = actor.Username, AtUtc = row.UpdatedUtc, BeforeJson = before, AfterJson = JsonSerializer.Serialize(row) }); await db.SaveChangesAsync(); await tx.CommitAsync(); }
        catch (DbUpdateConcurrencyException) { return ConflictResult(); }
        return RedirectToPage(new { id = row.Id });
    }
    private IActionResult ConflictResult() => StatusCode(409, "Record changed. Reload and try again.");
}
