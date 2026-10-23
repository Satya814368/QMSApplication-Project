
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

        [BindProperty]
        public List<string> SelectedRecordIds { get; set; } = new();

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
            var denied = await CheckDeleteAccessAsync();
            if (denied != null) return denied;
            if (string.IsNullOrWhiteSpace(id))
            {
                TempData["ErrorMessage"] = "Record ID is missing.";
                return RedirectToPage();
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var record = await _db.MDRecords
                .FirstOrDefaultAsync(x => x.RecordID == id);

            if (record == null)
            {
                TempData["ErrorMessage"] =
                    $"Record {id} was not found. No records were deleted.";

                return RedirectToPage();
            }

            await AddDeleteAuditsAsync(new[] { record });
            _db.MDRecords.Remove(record);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["SuccessMessage"] =
                $"Record {id} was deleted successfully.";

            return RedirectToPage();
        }

        
        // DELETE MULTIPLE SELECTED M&D RECORDS
        public async Task<IActionResult> OnPostBulkDeleteAsync()
        {
            var denied = await CheckDeleteAccessAsync();
            if (denied != null) return denied;
            if (SelectedRecordIds == null || SelectedRecordIds.Count == 0)
            {
                TempData["ErrorMessage"] =
                    "Please select at least one record to delete.";

                return RedirectToPage();
            }

            var selectedIds = SelectedRecordIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (selectedIds.Count == 0)
            {
                TempData["ErrorMessage"] =
                    "No valid Record IDs were selected.";

                return RedirectToPage();
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var recordsToDelete = await _db.MDRecords
                .Where(record => selectedIds.Contains(record.RecordID))
                .ToListAsync();

            if (recordsToDelete.Count == 0)
            {
                TempData["ErrorMessage"] =
                    "None of the selected records were found. No records were deleted.";

                return RedirectToPage();
            }

            await AddDeleteAuditsAsync(recordsToDelete);
            _db.MDRecords.RemoveRange(recordsToDelete);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["SuccessMessage"] =
                $"{recordsToDelete.Count} selected M&D record(s) deleted successfully.";

            return RedirectToPage();
        }

        private async Task<IActionResult?> CheckDeleteAccessAsync()
        {
            var username = HttpContext.Session.GetString("Username");
            if (string.IsNullOrWhiteSpace(username)) return RedirectToPage("/Login");
            var role = await _db.PortalUsers.AsNoTracking().Where(u => u.IsActive && u.Username == username)
                .Select(u => u.Role).FirstOrDefaultAsync();
            return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ? null : StatusCode(403);
        }

        private async Task AddDeleteAuditsAsync(IEnumerable<MDEntity> records)
        {
            var list = records.ToList();
            var ids = list.Select(r => r.Id).ToList();
            var tasks = await _db.DeliverableTasks.AsNoTracking().Where(t => ids.Contains(t.DeliverableId))
                .Include(t => t.Targets).ThenInclude(t => t.Steps).AsSplitQuery().ToListAsync();
            foreach (var record in list)
                _db.MDAuditLogs.Add(new MDAuditLog
                {
                    TimestampUtc = DateTime.UtcNow,
                    Username = HttpContext.Session.GetString("Username")!, RecordCode = record.RecordID,
                    Action = "Delete", ChangedField = "Deliverable and language tasks",
                    OldValue = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        Record = record,
                        Tasks = tasks.Where(t => t.DeliverableId == record.Id).Select(t => new
                        {
                            t.Language, t.AssignedUserId,
                            Targets = t.Targets.Select(x => new
                            {
                                x.TargetNumber, x.AssignedUserId, x.IsCancelled, x.CancellationReason, x.GnApprovalReference,
                                Steps = x.Steps.Select(s => new { s.StepNumber, s.Status, s.MinutesSpent, s.CompletedAtUtc })
                            })
                        })
                    })
                });
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
            var entities = _db.MDRecords
    .AsNoTracking()
    .OrderBy(x => x.Id)
    .ToList();

            var taskCounts = _db.DeliverableTasks.AsNoTracking()
                .GroupBy(t => t.DeliverableId)
                .Select(g => new { DeliverableId = g.Key, Count = g.Count() })
                .ToDictionary(g => g.DeliverableId, g => g.Count);

            Records = entities.Select(x => new MDRecord
            {
                RecordID = x.RecordID,
                DeliverableId = x.Id,
                TaskCount = taskCounts.GetValueOrDefault(x.Id),
                DepartmentName = x.DepartmentName,
                SNo = x.SNo,

                MilestoneName = x.MilestoneName,
                DeliverableName = x.DeliverableName,
                DeliverableType = x.DeliverableType,

                MilestoneTargetDate = x.MilestoneTargetDate.HasValue
                    ? x.MilestoneTargetDate.Value.ToString("dd-MM-yyyy")
                    : x.MilestoneTargetDateSource,

                DeliverableTargetDate = x.DeliverableTargetDate.HasValue
                    ? x.DeliverableTargetDate.Value.ToString("dd-MM-yyyy")
                    : x.DeliverableTargetDateSource,

                AssignedTo = x.AssignedTo,
                ScheduleDeviation = x.ScheduleDeviation,
                DeliverableActivity = x.DeliverableActivity,
                DeviationReason = x.DeviationReason,

                DeliverableActualCompletedDate =
                    x.DeliverableActualCompletedDate.HasValue
                        ? x.DeliverableActualCompletedDate.Value.ToString("dd-MM-yyyy")
                        : x.DeliverableActualCompletedDateSource,

                MilestoneAchievedDate =
                    x.MilestoneAchievedDate.HasValue
                        ? x.MilestoneAchievedDate.Value.ToString("dd-MM-yyyy")
                        : x.MilestoneAchievedDateSource,

                Status = x.Status,
                Remarks = x.Remarks,
                UGs = x.UGs
            }).ToList();

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
