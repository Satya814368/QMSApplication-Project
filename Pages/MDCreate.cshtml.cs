using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using QMSApplication.Data;
using System.ComponentModel.DataAnnotations;
using System.Data;

namespace QMSApplication.Pages;

public class MDCreateModel : PageModel
{
    private readonly QMSPortalDbContext _db;
    public MDCreateModel(QMSPortalDbContext db) => _db = db;
    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public string TasksJson { get; set; } = "[]";
    public bool ShowTasks { get; set; }
    public List<PortalUser> ActiveUsers { get; set; } = new();

    public class InputModel
    {
        [Required, StringLength(200)] public string MilestoneName { get; set; } = "";
        [Required, StringLength(50)] public string DepartmentName { get; set; } = "";
        [Required, StringLength(200)] public string DeliverableName { get; set; } = "";
        [Required] public string DeliverableType { get; set; } = "";
        [Required] public DateOnly? MilestoneTargetDate { get; set; }
        [Required] public DateOnly? DeliverableTargetDate { get; set; }
        [Required] public string AssignedTo { get; set; } = "";
        [StringLength(4000)] public string? Remarks { get; set; }
    }

    private async Task<IActionResult?> AccessCheckAsync()
    {
        if (string.IsNullOrWhiteSpace(HttpContext.Session.GetString("Username")))
            return RedirectToPage("/Login");
        var username = HttpContext.Session.GetString("Username");
        var actor = await _db.PortalUsers.AsNoTracking().Where(x => x.Username == username && x.IsActive)
            .Select(x => new { x.Role }).FirstOrDefaultAsync();
        return actor == null || string.Equals(actor.Role, "Guest", StringComparison.OrdinalIgnoreCase) ? StatusCode(403) : null;
    }
    private async Task LoadUsersAsync() => ActiveUsers = await _db.PortalUsers.AsNoTracking()
        .Where(x => x.IsActive).OrderBy(x => x.FullName)
        .Select(x => new PortalUser { Id = x.Id, Username = x.Username, FullName = x.FullName }).ToListAsync();

    private void ValidateDetails()
    {
        if (!Input.MilestoneTargetDate.HasValue)
            ModelState.AddModelError("Input.MilestoneTargetDate", "Milestone Target Date is required.");
        if (!Input.DeliverableTargetDate.HasValue)
            ModelState.AddModelError("Input.DeliverableTargetDate", "Deliverable Target Date is required.");
        if (!new[] { "FSW", "IS", "Labeling", "SV", "S&R", "App", "FWV" }.Contains(Input.DepartmentName))
            ModelState.AddModelError("Input.DepartmentName", "Select a valid department.");
        if (!new[] { "UGs", "Clarification Doc", "Data Sheets", "Other" }.Contains(Input.DeliverableType))
            ModelState.AddModelError("Input.DeliverableType", "Select a valid deliverable type.");
        if (!ActiveUsers.Any(x => x.Username == Input.AssignedTo))
            ModelState.AddModelError("Input.AssignedTo", "Select an active registered member.");
        if (string.IsNullOrWhiteSpace(Input.MilestoneName) || string.IsNullOrWhiteSpace(Input.DeliverableName))
            ModelState.AddModelError("", "Milestone and deliverable names are required.");
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var denied = await AccessCheckAsync(); if (denied != null) return denied;
        await LoadUsersAsync(); return Page();
    }

    public async Task<IActionResult> OnPostNextAsync()
    {
        var denied = await AccessCheckAsync(); if (denied != null) return denied;
        await LoadUsersAsync(); ValidateDetails();
        ShowTasks = ModelState.IsValid;
        return Page();
    }

    public async Task<IActionResult> OnPostBackAsync()
    {
        var denied = await AccessCheckAsync(); if (denied != null) return denied;
        await LoadUsersAsync(); return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var denied = await AccessCheckAsync(); if (denied != null) return denied;
        await LoadUsersAsync(); ValidateDetails();
        if (!ModelState.IsValid) return Page();
        ShowTasks = true;
        var username = HttpContext.Session.GetString("Username")!;
        var result = await LanguageTaskFactory.BuildAsync(_db, TasksJson, username);
        if (result.Error != null) { ModelState.AddModelError("", result.Error); return Page(); }

        // Serializable creation protects the legacy max+1 record-code allocator here.
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var ids = await _db.MDRecords.Select(r => r.RecordID).ToListAsync();
            int maxNumber = ids.Select(id => id.StartsWith("MD-") && int.TryParse(id[3..], out var n) ? n : 0)
                .DefaultIfEmpty(0).Max();
            var sns = await _db.MDRecords.Select(r => r.SNo).ToListAsync();
            var nextSNo = sns.Select(s => int.TryParse(s, out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
            var entity = new MDEntity
            {
                RecordID = $"MD-{maxNumber + 1:D4}", SNo = nextSNo.ToString(),
                DepartmentName = Input.DepartmentName.Trim(), MilestoneName = Input.MilestoneName.Trim(),
                DeliverableName = Input.DeliverableName.Trim(), DeliverableType = Input.DeliverableType,
                MilestoneTargetDate = Input.MilestoneTargetDate, DeliverableTargetDate = Input.DeliverableTargetDate,
                MilestoneTargetDateSource = Input.MilestoneTargetDate?.ToString("dd-MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture),
                DeliverableTargetDateSource = Input.DeliverableTargetDate?.ToString("dd-MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture),
                AssignedTo = Input.AssignedTo, Status = "Not Started", ScheduleDeviation = "No",
                DeliverableActivity = "N/A", DeviationReason = "N/A", Remarks = Input.Remarks,
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
                CreatedBy = username, UpdatedBy = username
            };
            _db.MDRecords.Add(entity);
            foreach (var task in result.Tasks) { task.Deliverable = entity; _db.DeliverableTasks.Add(task); }
            _db.MDAuditLogs.Add(new MDAuditLog
            {
                TimestampUtc = DateTime.UtcNow, Username = username, RecordCode = entity.RecordID,
                Action = "Create", ChangedField = "Deliverable and language tasks",
                NewValue = System.Text.Json.JsonSerializer.Serialize(new
                {
                    entity.DeliverableName,
                    Tasks = result.Tasks.Select(t => new { t.Language, t.AssignedUserId, Targets = t.Targets.Select(x => x.TargetNumber) })
                })
            });
            await _db.SaveChangesAsync(); await transaction.CommitAsync();
            TempData["SuccessMessage"] = $"Deliverable {entity.RecordID} and {result.Tasks.Count} language tasks saved.";
            return RedirectToPage("/MDTasks", new { id = entity.Id });
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            ModelState.AddModelError("", "Unable to save. No deliverable or tasks were saved. Please try again.");
            return Page();
        }
    }
}
