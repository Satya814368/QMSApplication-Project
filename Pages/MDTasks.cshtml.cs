using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Data;
using QMSApplication.Models;

namespace QMSApplication.Pages;

public class MDTasksModel : PageModel
{
    private readonly QMSPortalDbContext _db;
    private PortalUser? _actor;
    public MDTasksModel(QMSPortalDbContext db) => _db = db;
    public MDEntity Deliverable { get; set; } = null!;
    public List<DeliverableTask> Tasks { get; set; } = new();
    public List<PortalUser> ActiveUsers { get; set; } = new();
    public bool CanManage => _actor != null && (TaskWorkflow.CanManage(_actor.Role)
        || string.Equals(Deliverable?.CreatedBy, _actor.Username, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Deliverable?.AssignedTo, _actor.Username, StringComparison.OrdinalIgnoreCase));
    public bool CanUpdate(DeliverableTarget target) => CanManage || target.AssignedUserId == _actor?.Id;
    [BindProperty] public string? TasksJson { get; set; } = "[]";
    [BindProperty] public List<long> SelectedTargets { get; set; } = new();
    [BindProperty] public Dictionary<long, Guid> Versions { get; set; } = new();
    [BindProperty] public string UpdateAction { get; set; } = "Progress";
    [BindProperty, Range(1, 14)] public int StepNumber { get; set; } = 1;
    [BindProperty] public string StepStatus { get; set; } = "In Progress";
    [BindProperty, Range(0, 100000)] public int Hours { get; set; }
    [BindProperty, Range(0, 59)] public int Minutes { get; set; }
    [BindProperty] public long? NewAssignedUserId { get; set; }
    [BindProperty, StringLength(500)] public string? CancellationReason { get; set; }
    [BindProperty, StringLength(200)] public string? GnApprovalReference { get; set; }

    private async Task<IActionResult?> LoadAsync(long id)
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrWhiteSpace(username)) return RedirectToPage("/Login");
        _actor = await _db.PortalUsers.AsNoTracking().Where(x => x.Username == username && x.IsActive)
            .Select(x => new PortalUser { Id = x.Id, Username = x.Username, Role = x.Role }).FirstOrDefaultAsync();
        if (_actor == null) return StatusCode(403);
        var deliverable = await _db.MDRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (deliverable == null) return NotFound();
        // Direct URL access must not expose another user's assigned tasks.
        var hasAssignment = string.Equals(deliverable.AssignedTo, _actor.Username, StringComparison.OrdinalIgnoreCase)
            || await _db.DeliverableTasks.AnyAsync(t => t.DeliverableId == id &&
                (t.AssignedUserId == _actor.Id || t.Targets.Any(x => x.AssignedUserId == _actor.Id)));
        if (!TaskWorkflow.CanManage(_actor.Role) && !hasAssignment &&
            !string.Equals(deliverable.CreatedBy, _actor.Username, StringComparison.OrdinalIgnoreCase))
            return StatusCode(403);
        Deliverable = deliverable;
        ActiveUsers = await _db.PortalUsers.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.FullName)
            .Select(x => new PortalUser { Id = x.Id, Username = x.Username, FullName = x.FullName }).ToListAsync();
        Tasks = await _db.DeliverableTasks.AsNoTracking().Where(x => x.DeliverableId == id)
            .Include(x => x.Targets).ThenInclude(x => x.Steps)
            .OrderBy(x => x.Language).AsSplitQuery().ToListAsync();
        return null;
    }
    public string MemberName(long id) => ActiveUsers.FirstOrDefault(x => x.Id == id)?.FullName ?? $"Inactive member #{id}";
    public async Task<IActionResult> OnGetAsync(long id) => await LoadAsync(id) ?? Page();

    public async Task<IActionResult> OnPostAddAsync(long id)
    {
        var denied = await LoadAsync(id); if (denied != null) return denied;
        if (!CanManage) return StatusCode(403);
        if (!ModelState.IsValid) return Page();
        var result = await LanguageTaskFactory.BuildAsync(_db, TasksJson, _actor!.Username);
        if (result.Error != null) { ModelState.AddModelError("", result.Error); return Page(); }
        if (Tasks.Count + result.Tasks.Count > 40 || Tasks.Sum(t => t.Targets.Count) + result.Tasks.Sum(t => t.Targets.Count) > 400)
        { ModelState.AddModelError("", "A deliverable supports up to 40 language tasks and 400 targets."); return Page(); }
        if (result.Tasks.Any(t => Tasks.Any(x => x.Language.Equals(t.Language, StringComparison.OrdinalIgnoreCase))))
        { ModelState.AddModelError("", "That language already exists. Add only new language tasks."); return Page(); }
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            foreach (var task in result.Tasks) { task.DeliverableId = id; _db.DeliverableTasks.Add(task); }
            AddAudit("Language tasks added", null, result.Tasks.Select(t => new
                { t.Language, t.AssignedUserId, Targets = t.Targets.Select(x => x.TargetNumber) }));
            await _db.SaveChangesAsync(); await transaction.CommitAsync();
            TempData["SuccessMessage"] = "Language tasks added.";
            return RedirectToPage(new { id });
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            ModelState.AddModelError("", "The tasks could not be saved. A language may have been added by someone else. Reload and try again.");
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUpdateAsync(long id)
    {
        var denied = await LoadAsync(id); if (denied != null) return denied;
        if (!ModelState.IsValid) return Page();
        var ids = SelectedTargets.Distinct().OrderBy(x => x).ToList();
        if (ids.Count is < 1 or > 400) { ModelState.AddModelError("", "Select 1–400 targets."); return Page(); }
        if (!new[] { "Progress", "Assign", "Cancel" }.Contains(UpdateAction)) return BadRequest();
        if (UpdateAction != "Progress" && !CanManage) return StatusCode(403);
        var targets = await _db.DeliverableTargets.Include(x => x.Steps).Include(x => x.Task)
            .Where(x => x.Task.DeliverableId == id && ids.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync();
        if (targets.Count != ids.Count) return BadRequest();
        if (targets.Any(x => !CanUpdate(x))) return StatusCode(403);
        if (targets.Any(x => !Versions.TryGetValue(x.Id, out var version) || version != x.ConcurrencyToken))
        {
            ModelState.AddModelError("", "A selected target has changed. Reload this page and review the latest values before saving.");
            return Page();
        }
        if (UpdateAction == "Progress" && (!new[] { "Not Started", "In Progress", "Completed" }.Contains(StepStatus)
            || targets.Any(x => x.IsCancelled)))
        { ModelState.AddModelError("", "Choose a valid step status and select active targets only."); return Page(); }
        if (UpdateAction == "Assign" && !ActiveUsers.Any(x => x.Id == NewAssignedUserId))
        { ModelState.AddModelError("", "Select an active member for reassignment."); return Page(); }
        if (UpdateAction == "Cancel" && (string.IsNullOrWhiteSpace(CancellationReason) || string.IsNullOrWhiteSpace(GnApprovalReference)))
        { ModelState.AddModelError("", "Cancellation requires a reason and GN approval reference."); return Page(); }
        if (UpdateAction == "Progress" && targets.Any(x => x.Steps.Count != 14 || !x.Steps.Any(s => s.StepNumber == StepNumber)))
        { ModelState.AddModelError("", "A selected target has incomplete step data. Contact the administrator."); return Page(); }

        var allocation = TaskWorkflow.SplitMinutes((long)Hours * 60 + Minutes, targets.Count);
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                var before = Snapshot(target);
                _db.Entry(target).Property(x => x.ConcurrencyToken).OriginalValue = Versions[target.Id];
                if (UpdateAction == "Progress")
                {
                    var step = target.Steps.Single(x => x.StepNumber == StepNumber);
                    step.Status = StepStatus;
                    step.CompletedAtUtc = StepStatus == "Completed" ? step.CompletedAtUtc ?? DateTime.UtcNow : null;
                    step.MinutesSpent += allocation[i];
                }
                else if (UpdateAction == "Assign") target.AssignedUserId = NewAssignedUserId!.Value;
                else
                {
                    target.IsCancelled = true; target.CancellationReason = CancellationReason!.Trim();
                    target.GnApprovalReference = GnApprovalReference!.Trim(); target.CancelledAtUtc = DateTime.UtcNow;
                }
                target.ConcurrencyToken = Guid.NewGuid(); target.UpdatedAtUtc = DateTime.UtcNow; target.UpdatedBy = _actor!.Username;
                AddAudit($"Target {target.TargetNumber} ({target.Task.Language}): {UpdateAction}", before, Snapshot(target));
            }
            await _db.SaveChangesAsync(); await transaction.CommitAsync();
            TempData["SuccessMessage"] = UpdateAction == "Progress"
                ? $"Updated {targets.Count} target(s). {TaskWorkflow.Duration((long)Hours * 60 + Minutes)} additional time distributed in total."
                : $"Updated {targets.Count} target(s).";
            return RedirectToPage(new { id });
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            ModelState.AddModelError("", "Someone changed a selected target. No updates were saved. Reload and review before trying again.");
            return Page();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            ModelState.AddModelError("", "Unable to save. No selected targets were updated. Please reload and try again.");
            return Page();
        }
    }

    private static object Snapshot(DeliverableTarget t) => new
    {
        t.TargetNumber, t.AssignedUserId, t.IsCancelled, t.CancellationReason, t.GnApprovalReference,
        Steps = t.Steps.OrderBy(x => x.StepNumber).Select(x => new { x.StepNumber, x.Status, x.MinutesSpent, x.CompletedAtUtc }).ToArray()
    };
    private void AddAudit(string field, object? before, object after) => _db.MDAuditLogs.Add(new MDAuditLog
    {
        TimestampUtc = DateTime.UtcNow, Username = _actor!.Username, RecordCode = Deliverable.RecordID,
        Action = "Update", ChangedField = field.Length > 200 ? field[..200] : field,
        OldValue = before == null ? null : JsonSerializer.Serialize(before), NewValue = JsonSerializer.Serialize(after)
    });
}
