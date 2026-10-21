
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;

namespace QMSApplication.Pages
{
    public class MDModel : PageModel
    {
        private readonly QMSPortalDbContext _db;

        public MDModel(QMSPortalDbContext db)
        {
            _db = db;
        }

        public List<MDRecord> Records { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? MilestoneFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? AssignedToFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? StatusFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? DeliverableTypeFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? ScheduleDeviationFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? DepartmentFilter { get; set; }

        public List<string> MilestoneNames { get; set; } = new();
        public List<string> AssignedToNames { get; set; } = new();
        public List<string> Statuses { get; set; } = new();
        public List<string> DeliverableTypes { get; set; } = new();
        public List<string> ScheduleDeviations { get; set; } = new();
        public List<string> Departments { get; set; } = new();

        // GET M&D RECORDS AND APPLY FILTERS
        public void OnGet()
        {
            LoadRecords();

            // FILTER VALUES
            MilestoneNames = Records
                .Select(x => x.MilestoneName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            AssignedToNames = Records
                .Select(x => x.AssignedTo)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            Statuses = Records
                .Select(x => x.Status)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            DeliverableTypes = Records
                .Select(x => x.DeliverableType)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            ScheduleDeviations = Records
                .Select(x => x.ScheduleDeviation)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            Departments = Records
                .Select(x => x.DepartmentName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            // TEXT SEARCH
            if (!string.IsNullOrWhiteSpace(Search))
            {
                string searchText = Search.Trim();

                Records = Records
                    .Where(x =>
                        ContainsIgnoreCase(x.MilestoneName, searchText) ||
                        ContainsIgnoreCase(x.DeliverableName, searchText) ||
                        ContainsIgnoreCase(x.AssignedTo, searchText) ||
                        ContainsIgnoreCase(x.Status, searchText))
                    .ToList();
            }

            // MILESTONE FILTER
            if (!string.IsNullOrWhiteSpace(MilestoneFilter))
            {
                Records = Records
                    .Where(x => string.Equals(
                        x.MilestoneName?.Trim(),
                        MilestoneFilter.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // ASSIGNED TO FILTER
            if (!string.IsNullOrWhiteSpace(AssignedToFilter))
            {
                Records = Records
                    .Where(x => string.Equals(
                        x.AssignedTo?.Trim(),
                        AssignedToFilter.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // STATUS FILTER
            if (!string.IsNullOrWhiteSpace(StatusFilter))
            {
                Records = Records
                    .Where(x => string.Equals(
                        x.Status?.Trim(),
                        StatusFilter.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // DELIVERABLE TYPE FILTER
            if (!string.IsNullOrWhiteSpace(DeliverableTypeFilter))
            {
                Records = Records
                    .Where(x => string.Equals(
                        x.DeliverableType?.Trim(),
                        DeliverableTypeFilter.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // SCHEDULE DEVIATION FILTER
            if (!string.IsNullOrWhiteSpace(ScheduleDeviationFilter))
            {
                Records = Records
                    .Where(x => string.Equals(
                        x.ScheduleDeviation?.Trim(),
                        ScheduleDeviationFilter.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // DEPARTMENT FILTER
            if (!string.IsNullOrWhiteSpace(DepartmentFilter))
            {
                Records = Records
                    .Where(x => string.Equals(
                        x.DepartmentName?.Trim(),
                        DepartmentFilter.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        // DELETE ONLY THE SELECTED M&D RECORD
        public async Task<IActionResult> OnPostDeleteAsync(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                TempData["ErrorMessage"] = "Record ID is missing.";
                return RedirectToPage();
            }

            var record = await _db.MDRecords
                .FirstOrDefaultAsync(x => x.RecordID == id);

            if (record == null)
            {
                TempData["ErrorMessage"] =
                    $"Record {id} was not found. No records were deleted.";

                return RedirectToPage();
            }

            _db.MDRecords.Remove(record);
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] =
                $"Record {id} was deleted successfully.";

            return RedirectToPage();
        }

        // CASE-INSENSITIVE SEARCH
        private static bool ContainsIgnoreCase(
            string? source,
            string searchText)
        {
            return !string.IsNullOrWhiteSpace(source) &&
                   source.Contains(
                       searchText,
                       StringComparison.OrdinalIgnoreCase);
        }

        // LOAD RECORDS FROM MYSQL
        private void LoadRecords()
        {
            Records = _db.MDRecords
                .AsNoTracking()
                .OrderBy(x => x.Id)
                .Select(x => new MDRecord
                {
                    RecordID = x.RecordID,
                    DepartmentName = x.DepartmentName,
                    SNo = x.SNo,

                    MilestoneName = x.MilestoneName,
                    DeliverableName = x.DeliverableName,
                    DeliverableType = x.DeliverableType,

                    MilestoneTargetDate = x.MilestoneTargetDateSource,
                    DeliverableTargetDate = x.DeliverableTargetDateSource,

                    AssignedTo = x.AssignedTo,
                    ScheduleDeviation = x.ScheduleDeviation,
                    DeliverableActivity = x.DeliverableActivity,
                    DeviationReason = x.DeviationReason,

                    DeliverableActualCompletedDate =
                        x.DeliverableActualCompletedDateSource,

                    MilestoneAchievedDate = x.MilestoneAchievedDateSource,

                    Status = x.Status,
                    Remarks = x.Remarks,
                    UGs = x.UGs
                })
                .ToList();

            // STATUS NORMALIZATION
            foreach (var record in Records)
            {
                if (record.Status != null &&
                    (record.Status.Equals(
                         "Deffered",
                         StringComparison.OrdinalIgnoreCase) ||
                     record.Status.Equals(
                         "Defferred",
                         StringComparison.OrdinalIgnoreCase)))
                {
                    record.Status = "Deferred";
                }
            }

            // OVERDUE CALCULATION
            foreach (var record in Records)
            {
                record.IsOverdue = false;

                if (string.IsNullOrWhiteSpace(record.DeliverableTargetDate))
                    continue;

                string status = record.Status?.Trim() ?? string.Empty;

                if (status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
                    status.Equals("Deferred", StringComparison.OrdinalIgnoreCase) ||
                    status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (DateTime.TryParse(
                    record.DeliverableTargetDate.Trim(),
                    out DateTime targetDate))
                {
                    record.IsOverdue = targetDate.Date < DateTime.Today;
                }
            }
        }
    }
}
