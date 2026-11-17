using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using QMSApplication.Security;

namespace QMSApplication.Pages;
public class MDEditModel : PageModel
{
    private readonly QMSPortalDbContext _db;
    public MDEditModel(QMSPortalDbContext db) => _db = db;
    [BindProperty(SupportsGet = true)]
    public string? Id { get; set; }

    [BindProperty]
    public Guid Version { get; set; }

    [BindProperty]
    public EditInput Input { get; set; } = new();
    public List<PortalUser> Assignees { get; set; } = new();
    public class EditInput
    {
        [Required, StringLength(200)]
        public string MilestoneName { get; set; } = "";
        [Required, StringLength(200)]
        public string DeliverableName { get; set; } = "";
        [Required, StringLength(50)]
        public string DepartmentName { get; set; } = "";
        [Required]
        public string DeliverableType { get; set; } = "";
        [Required]
        public DateOnly? MilestoneTargetDate { get; set; }

        [Required]
        public DateOnly? DeliverableTargetDate { get; set; }

        public DateOnly? DeliverableActualCompletedDate { get; set; }

        [Required]
        public string AssignedTo { get; set; } = "";
        [Required]
        public string Status { get; set; } = "Not Started";
        [StringLength(4000)]
        public string? Remarks { get; set; }
    }

    private async Task<MDEntity?> Load()
    {
        var access = new AccessService(_db);
        var actor = await access.Current(HttpContext);
        if (actor == null)
            return null;
        var record = await _db.MDRecords.FirstOrDefaultAsync(x => x.RecordID == Id);
        if (record == null || !await access.Manage(actor, record))
            return null;
        if (record.TeamId.HasValue)
            Assignees = await access.Assignees(record.TeamId.Value).OrderBy(x => x.FullName).ToListAsync();
        return record;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var record = await Load();
        if (record == null)
            return RedirectToPage("/AccessDenied");
        Version = record.ConcurrencyToken;
        Input = new()
        {
            MilestoneName = record.MilestoneName ?? "",
            DeliverableName = record.DeliverableName ?? "",
            DepartmentName = record.DepartmentName ?? "",
            DeliverableType = record.DeliverableType ?? "",
            MilestoneTargetDate = record.MilestoneTargetDate,
            DeliverableTargetDate = record.DeliverableTargetDate,
            DeliverableActualCompletedDate = record.DeliverableActualCompletedDate,
            AssignedTo = record.AssignedTo ?? "",
            Status = record.Status ?? "Not Started",
            Remarks = record.Remarks
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var record = await Load();
        if (record == null)
            return RedirectToPage("/AccessDenied");
        if (!Assignees.Any(x => x.Username == Input.AssignedTo))
            ModelState.AddModelError("", "Choose an approved assignee in the owning team. Map legacy records to a team first.");
        if (!new[]
        {
            "Not Started",
            "In Progress",
            "Completed",
            "Deferred",
            "Cancelled"
        }.Contains(Input.Status))
            ModelState.AddModelError("", "Choose a valid status.");
        if (!new[]
        {
            "UGs",
            "Clarification Doc",
            "Data Sheets",
            "Other"
        }.Contains(Input.DeliverableType))
            ModelState.AddModelError("", "Choose a valid deliverable type.");
        if (record.ConcurrencyToken != Version)
            ModelState.AddModelError("", "This record changed. Refresh and review before saving.");
        if (!ModelState.IsValid)
            return Page();
        var before = JsonSerializer.Serialize(new { record.MilestoneName, record.DeliverableName, record.DepartmentName, record.DeliverableType, record.AssignedTo, record.Status, record.MilestoneTargetDate, record.DeliverableTargetDate, record.DeliverableActualCompletedDate, record.Remarks });
        record.MilestoneName = Input.MilestoneName.Trim();
        record.DeliverableName = Input.DeliverableName.Trim();
        record.DepartmentName = Input.DepartmentName.Trim();
        record.DeliverableType = Input.DeliverableType;
        record.AssignedTo = Input.AssignedTo;
        record.Status = Input.Status;
        record.MilestoneTargetDate = Input.MilestoneTargetDate;
        record.DeliverableTargetDate = Input.DeliverableTargetDate;
        record.DeliverableActualCompletedDate = Input.DeliverableActualCompletedDate;
        record.MilestoneTargetDateSource = Input.MilestoneTargetDate?.ToString("dd-MMM-yyyy");
        record.DeliverableTargetDateSource = Input.DeliverableTargetDate?.ToString("dd-MMM-yyyy");
        record.DeliverableActualCompletedDateSource = Input.DeliverableActualCompletedDate?.ToString("dd-MMM-yyyy");
        record.Remarks = Input.Remarks;
        record.UpdatedAtUtc = DateTime.UtcNow;
        record.UpdatedBy = HttpContext.Session.GetString("Username");
        record.ConcurrencyToken = Guid.NewGuid();
        _db.MDAuditLogs.Add(new MDAuditLog { TimestampUtc = DateTime.UtcNow, Username = record.UpdatedBy!, RecordCode = record.RecordID, Action = "Update", ChangedField = "Deliverable details", OldValue = before, NewValue = JsonSerializer.Serialize(Input) });
        try
        {
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            TempData["SuccessMessage"] = "Deliverable updated.";
            return RedirectToPage("/md");
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError("", "Record changed or could not be saved. Refresh before retrying.");
            return Page();
        }
    }
}
