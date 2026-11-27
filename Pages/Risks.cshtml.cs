using ClosedXML.Excel;
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
    public record Choice(long Id, long TeamId, string Label);
    public List<Choice> OwnerChoices { get; } = new();
    public List<Choice> DeliverableChoices { get; } = new();
    public string? ErrorMessage { get; set; }
    public string? KindFilter { get; set; }
    public string? Search { get; set; }
    public string? StatusFilter { get; set; }
    public string? Sort { get; set; }
    public int OverdueCount { get; set; }
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(330));
    private IQueryable<RiskIssue> Filter(PortalUser actor, string? kind, string? search, string? status, string? sort)
    {
        var q = RiskIssueAccess.Visible(db, actor).AsNoTracking();
        if (kind is "Risk" or "Issue") q = q.Where(x => x.Kind == kind);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Title.Contains(search) || x.Description.Contains(search));
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        return sort switch { "due" => q.OrderBy(x => x.DueDate).ThenBy(x => x.Id), "score" => q.OrderByDescending(x => x.Probability * x.Impact).ThenByDescending(x => x.Id), _ => q.OrderByDescending(x => x.Id) };
    }
    public async Task<IActionResult> OnGetExportAsync(string? kind, string? search, string? status, string? sort)
    {
        var actor = await new AccessService(db).Current(HttpContext);
        if (actor == null) return RedirectToPage("/Login");
        if (AccessService.Role(actor) == "Guest") return StatusCode(403);
        var rows = await Filter(actor, kind, search, status, sort).ToListAsync();
        using var book = new XLWorkbook(); var sheet = book.Worksheets.Add("Register");
        string[] headers = ["ID", "Type", "Title", "Category", "Team ID", "Owner ID", "Probability", "Impact", "Score", "Severity", "Status", "Due", "Action"];
        for (int i=0;i<headers.Length;i++) sheet.Cell(1,i+1).Value=headers[i];
        int n=2; foreach(var r in rows) { sheet.Cell(n,1).Value=r.Id; sheet.Cell(n,2).Value=r.Kind; sheet.Cell(n,3).Value=r.Title; sheet.Cell(n,4).Value=r.Category; sheet.Cell(n,5).Value=r.TeamId; sheet.Cell(n,6).Value=r.OwnerId; if(r.Kind=="Risk") { sheet.Cell(n,7).Value=r.Probability; sheet.Cell(n,8).Value=r.Impact; sheet.Cell(n,9).Value=r.Score; sheet.Cell(n,10).Value=r.Severity; } sheet.Cell(n,11).Value=r.Status; sheet.Cell(n,12).Value=r.DueDate.ToString("yyyy-MM-dd"); sheet.Cell(n,13).Value=r.ActionPlan; n++; }
        sheet.Row(1).Style.Font.Bold=true; sheet.SheetView.FreezeRows(1); sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream(); book.SaveAs(stream); return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "RiskIssueRegister.xlsx");
    }
    public RiskIssue? Edit { get; set; }
    public bool CanManage { get; set; }
    public long ActorId { get; set; }
    public bool CanReport { get; set; }
    public string[] Categories { get; } = ["Schedule", "Quality", "Resource", "Technical", "Operational", "Other"];
    public async Task<IActionResult> OnGetAsync(long? id, string? kind = null, string? search = null, string? status = null, string? sort = null)
    {
        var access = new AccessService(db); var actor = await access.Current(HttpContext);
        if (actor == null) return RedirectToPage("/Login");
        ActorId = actor.Id; CanManage = AccessService.CanCreate(actor); CanReport = CanManage || AccessService.Role(actor) == "User";
        var q = RiskIssueAccess.Visible(db, actor).AsNoTracking();
        if (AccessService.Role(actor) == "Guest") return StatusCode(403);
        KindFilter=kind; Search=search; StatusFilter=status; Sort=sort;
        Rows = await Filter(actor, kind, search, status, sort).ToListAsync();
        var today = Today;
        OverdueCount = await q.CountAsync(x => x.DueDate < today && (x.Status == "Open" || x.Status == "In Progress"));
        if (id.HasValue) { Edit = await q.SingleOrDefaultAsync(x => x.Id == id); if (Edit == null) return NotFound(); if (CanManage) History = await db.RiskIssueAudits.AsNoTracking().Where(x => x.RiskIssueId == id).OrderByDescending(x => x.Id).ToListAsync(); }
        var teamIds = db.TeamMemberships.Where(x => x.UserId == actor.Id).Select(x => x.TeamId);
        Teams = CanManage ? await access.ManagedTeams(actor).ToListAsync() : await db.Teams.Where(x => teamIds.Contains(x.Id)).ToListAsync();
        foreach(var t in Teams)
        {
            var owners = CanManage ? await access.Assignees(t.Id).OrderBy(x => x.FullName).ToListAsync() : new List<PortalUser> { actor };
            OwnerChoices.AddRange(owners.Select(x => new Choice(x.Id,t.Id,$"{x.FullName} ({x.Username})")));
        }
        var allowedTeams = Teams.Select(x => x.Id).ToList();
        var records = await access.Records(actor).Where(x => x.TeamId.HasValue && allowedTeams.Contains(x.TeamId.Value)).Select(x => new {x.Id,x.TeamId,x.RecordID,x.DeliverableName}).ToListAsync();
        DeliverableChoices.AddRange(records.Select(x => new Choice(x.Id,x.TeamId!.Value,$"{x.RecordID} - {x.DeliverableName}")));
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
            if (!ModelState.IsValid || row.OwnerId != actor.Id || row.Status == "Closed" || actionPlan == null || actionPlan.Length > 4000 || status is not ("Open" or "In Progress" or "Resolved")) return BadRequest("Invalid action update.");
            if (status == "Resolved" && string.IsNullOrWhiteSpace(actionPlan)) return BadRequest("Resolution action required.");
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
