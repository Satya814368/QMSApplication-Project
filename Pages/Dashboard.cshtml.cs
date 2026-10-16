
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using System.Globalization;

namespace QMSApplication.Pages
{
    public class DashboardModel : PageModel
    {
        private readonly QMSPortalDbContext _db;

        public DashboardModel(QMSPortalDbContext db)
        {
            _db = db;
        }

        public string? Username { get; set; }

        public int PendingCount { get; set; }
        public int InProgressCount { get; set; }
        public int CompletedCount { get; set; }
        public int DeferredCount { get; set; }
        public int CancelledCount { get; set; }

        public List<PortalUser> ActiveUsers { get; set; } = new();

        public List<string> MilestoneNames { get; set; } = new();
        public List<int> MilestoneCounts { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public DateOnly? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateOnly? ToDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? AssignedTo { get; set; }

        public async Task<IActionResult> OnGetAsync(string? username)
        {
            string? loggedInUser =
                HttpContext.Session.GetString("LoggedInUser");

            if (string.IsNullOrEmpty(loggedInUser))
            {
                return RedirectToPage("/Login");
            }

            Username = loggedInUser;

            await LoadMDCountsAsync();
            await LoadCompletedUGChartAsync();

            return Page();
        }

        private async Task LoadMDCountsAsync()
        {
            var statuses = await _db.MDRecords
                .AsNoTracking()
                .Select(x => x.Status)
                .ToListAsync();

            PendingCount = statuses.Count(x =>
                string.Equals(
                    x, "Not Started",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    x, "Deferred",
                    StringComparison.OrdinalIgnoreCase));

            InProgressCount = statuses.Count(x =>
                string.Equals(
                    x, "In Progress",
                    StringComparison.OrdinalIgnoreCase));

            CompletedCount = statuses.Count(x =>
                string.Equals(
                    x, "Completed",
                    StringComparison.OrdinalIgnoreCase));

            DeferredCount = statuses.Count(x =>
                string.Equals(
                    x, "Deferred",
                    StringComparison.OrdinalIgnoreCase));

            CancelledCount = statuses.Count(x =>
                string.Equals(
                    x, "Cancelled",
                    StringComparison.OrdinalIgnoreCase));
        }

        
private async Task LoadCompletedUGChartAsync()
        {
            // Retrieve completed UGs from the database.
            var completedUGRecords = await _db.MDRecords
                .AsNoTracking()
                .Where(x =>
                    x.DeliverableType != null &&
                    x.DeliverableType.Trim() == "UGs" &&
                    x.Status != null &&
                    x.Status.Trim() == "Completed")
                .Select(x => new
                {
                    x.MilestoneName,
                    x.AssignedTo,
                    x.DeliverableActualCompletedDate
                })
                .ToListAsync();

            // Populate the Assigned To dropdown.
            ActiveUsers = completedUGRecords
                .Where(x => !string.IsNullOrWhiteSpace(x.AssignedTo))
                .Select(x => x.AssignedTo!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .Select(x => new PortalUser
                {
                    Username = x,
                    FullName = x
                })
                .ToList();

            // Start with records that have a milestone name.
            var filteredRecords = completedUGRecords
                .Where(x => !string.IsNullOrWhiteSpace(x.MilestoneName));

            // From Date filter.
            if (FromDate.HasValue)
            {
                DateOnly fromDate = FromDate.Value;

                filteredRecords = filteredRecords.Where(x =>
                    TryParseCompletedDate(
                        Convert.ToString(x.DeliverableActualCompletedDate),
                        out DateOnly completedDate) &&
                    completedDate >= fromDate);
            }

            // To Date filter, including the selected date.
            if (ToDate.HasValue)
            {
                DateOnly toDate = ToDate.Value;

                filteredRecords = filteredRecords.Where(x =>
                    TryParseCompletedDate(
                        Convert.ToString(x.DeliverableActualCompletedDate),
                        out DateOnly completedDate) &&
                    completedDate <= toDate);
            }

            // Assigned To filter.
            if (!string.IsNullOrWhiteSpace(AssignedTo))
            {
                string selectedUser = AssignedTo.Trim();

                filteredRecords = filteredRecords.Where(x =>
                    string.Equals(
                        x.AssignedTo?.Trim(),
                        selectedUser,
                        StringComparison.OrdinalIgnoreCase));
            }

            // Group completed UGs by milestone.
            var results = filteredRecords
                .GroupBy(x => x.MilestoneName!.Trim())
                .Select(g => new
                {
                    MilestoneName = g.Key,
                    Count = g.Count()
                })
                .OrderBy(x => x.MilestoneName)
                .ToList();

            // Supply the chart labels and values.
            MilestoneNames = results
                .Select(x => x.MilestoneName)
                .ToList();

            MilestoneCounts = results
                .Select(x => x.Count)
                .ToList();
        }

           
        
              
        private static bool TryParseCompletedDate(
            string? dateText,
            out DateOnly completedDate)
        {
            completedDate = default;

            if (string.IsNullOrWhiteSpace(dateText))
            {
                return false;
            }

            string value = dateText.Trim();

            string[] formats =
            {
                "yyyy-MM-dd",
                "yyyy-M-d",
                "dd-MM-yyyy",
                "d-M-yyyy",
                "dd/MM/yyyy",
                "d/M/yyyy",
                "MM/dd/yyyy",
                "M/d/yyyy",
                "yyyy/MM/dd",
                "yyyy/M/d",
                "dd-MM-yy",
                "d-M-yy"
            };

            foreach (string format in formats)
            {
                if (DateOnly.TryParseExact(
                    value,
                    format,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out completedDate))
                {
                    return true;
                }
            }

            // Fallback for other recognizable date strings.
            if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsedDate))
            {
                completedDate =
                    DateOnly.FromDateTime(parsedDate);

                return true;
            }

            return false;
        }

        public IActionResult OnPostLogout()
        {
            HttpContext.Session.Clear();

            return RedirectToPage("/Login");
        }
    }
}
