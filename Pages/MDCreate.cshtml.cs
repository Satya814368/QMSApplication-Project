
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using System.ComponentModel.DataAnnotations;

namespace QMSApplication.Pages
{
    public class MDCreateModel : PageModel
    {
        private readonly QMSPortalDbContext _db;

        public MDCreateModel(QMSPortalDbContext db)
        {
            _db = db;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public class InputModel
        {
            [Required]
            public string MilestoneName { get; set; } = "";

            [Required]
            public string DeliverableName { get; set; } = "";

            [Required]
            public string DeliverableType { get; set; } = "";

            public DateOnly? MilestoneTargetDate { get; set; }

            [Required]
            public DateOnly? DeliverableTargetDate { get; set; }

            [Required]
            public string AssignedTo { get; set; } = "";

            [Required]
            public string Status { get; set; } = "Not Started";

            [Required]
            public string ScheduleDeviation { get; set; } = "No";

            public string? DeliverableActivity { get; set; }

            public string? DeviationReason { get; set; }

            public string? Remarks { get; set; }
        }

        public IActionResult OnGet()
        {
            if (string.IsNullOrWhiteSpace(
                HttpContext.Session.GetString("LoggedInUser")))
            {
                return RedirectToPage("/Login");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrWhiteSpace(
                HttpContext.Session.GetString("LoggedInUser")))
            {
                return RedirectToPage("/Login");
            }

            if (!ModelState.IsValid)
                return Page();

            string[] allowedStatuses =
            {
                "Not Started", "In Progress", "Completed",
                "Deferred", "Cancelled"
            };

            string[] allowedTypes =
            {
                "UGs", "Clarification Doc", "Data Sheets", "Other"
            };

            if (!allowedStatuses.Contains(Input.Status) ||
                !allowedTypes.Contains(Input.DeliverableType) ||
                (Input.ScheduleDeviation != "Yes" &&
                 Input.ScheduleDeviation != "No"))
            {
                ModelState.AddModelError("", "Please select valid options.");
                return Page();
            }

            if (Input.ScheduleDeviation == "Yes" &&
                (string.IsNullOrWhiteSpace(Input.DeliverableActivity) ||
                 string.IsNullOrWhiteSpace(Input.DeviationReason)))
            {
                ModelState.AddModelError(
                    "", "Activity and reason are required for a schedule deviation.");
                return Page();
            }

            string username =
                HttpContext.Session.GetString("LoggedInUser")!;

            await using var transaction =
                await _db.Database.BeginTransactionAsync();

            try
            {
                // Generate the next RecordID.
                // This basic approach should be strengthened with a database
                // unique constraint before allowing concurrent creation.
                var existingIds = await _db.MDRecords
                    .Select(r => r.RecordID)
                    .ToListAsync();

                int maxNumber = existingIds
                    .Select(id =>
                    {
                        if (id != null &&
                            id.StartsWith("MD-") &&
                            int.TryParse(id.Substring(3), out int n))
                            return n;

                        return 0;
                    })
                    .DefaultIfEmpty(0)
                    .Max();

                string recordId = $"MD-{maxNumber + 1:D4}";

                var existingSNos = await _db.MDRecords
      .Select(r => r.SNo)
      .ToListAsync();

                int nextSNo = existingSNos
                    .Select(s => int.TryParse(s, out var number) ? number : 0)
                    .DefaultIfEmpty(0)
                    .Max() + 1;

                var entity = new MDEntity
                {
                    RecordID = recordId,
                    SNo = nextSNo.ToString(),
                    MilestoneName = Input.MilestoneName.Trim(),
                    DeliverableName = Input.DeliverableName.Trim(),
                    DeliverableType = Input.DeliverableType,
                    MilestoneTargetDate = Input.MilestoneTargetDate,
                    DeliverableTargetDate = Input.DeliverableTargetDate,
                    AssignedTo = Input.AssignedTo.Trim(),
                    Status = Input.Status,
                    ScheduleDeviation = Input.ScheduleDeviation,
                    DeliverableActivity =
                        Input.ScheduleDeviation == "Yes"
                            ? Input.DeliverableActivity
                            : "N/A",
                    DeviationReason =
                        Input.ScheduleDeviation == "Yes"
                            ? Input.DeviationReason
                            : "N/A",
                    Remarks = Input.Remarks,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedBy = username,
                    UpdatedAtUtc = DateTime.UtcNow,
                    UpdatedBy = username,
                    ConcurrencyToken = Guid.NewGuid()
                };

                if (Input.Status == "Completed")
                    entity.DeliverableActualCompletedDate = DateOnly.FromDateTime(DateTime.Today);

                _db.MDRecords.Add(entity);

                _db.MDAuditLogs.Add(new MDAuditLog
                {
                    TimestampUtc = DateTime.UtcNow,
                    Username = username,
                    RecordCode = recordId,
                    Action = "Create",
                    ChangedField = "New deliverable",
                    NewValue = $"{entity.MilestoneName} - {entity.DeliverableName}"
                });

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["SuccessMessage"] =
                    $"Deliverable {recordId} created successfully.";

                return RedirectToPage("/MD");
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();

                ModelState.AddModelError(
                    "", "Unable to save the deliverable. Check required database fields and try again.");

                return Page();
            }
        }
    }
}
