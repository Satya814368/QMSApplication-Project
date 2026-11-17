using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;

namespace QMSApplication.Pages
{
    public class CompletedUGModel : PageModel
    {
        private readonly QMSPortalDbContext _db;

        public CompletedUGModel(QMSPortalDbContext db)
        {
            _db = db;
        }

        public List<PortalUser> ActiveUsers { get; set; } = new();

        public List<string> MilestoneNames { get; set; } = new();

        public List<int> MilestoneCounts { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public DateOnly? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateOnly? ToDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? AssignedTo { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var username =
                HttpContext.Session.GetString("Username");

            if (string.IsNullOrWhiteSpace(username))
            {
                return RedirectToPage("/Login");
            }

            var access = new QMSApplication.Security.AccessService(_db);
            var actor = await access.Current(HttpContext);
            if (actor == null) return RedirectToPage("/Login");
            // Load only users referenced by visible records
            var visibleRecords = access.Records(actor);
            ActiveUsers = await _db.PortalUsers
                .AsNoTracking()
                .Where(x => x.IsActive && x.IsApproved && visibleRecords.Any(r => r.AssignedTo == x.Username || r.AssignedTo == x.FullName))
                .OrderBy(x => x.FullName)
                .ToListAsync();

            // Start with completed UG records
            var query = access.Records(actor)
                .AsNoTracking()
                .Where(x =>
                    x.DeliverableType != null &&
                    x.DeliverableType.Trim() == "UGs" &&
                    x.Status != null &&
                    x.Status.Trim() == "Completed");

            // From Date filter
            if (FromDate.HasValue)
            {
                query = query.Where(x =>
                    x.DeliverableActualCompletedDate.HasValue &&
                    x.DeliverableActualCompletedDate.Value
                        >= FromDate.Value);
            }

            // To Date filter
            if (ToDate.HasValue)
            {
                query = query.Where(x =>
                    x.DeliverableActualCompletedDate.HasValue &&
                    x.DeliverableActualCompletedDate.Value
                        <= ToDate.Value);
            }

            // Assigned To filter
            if (!string.IsNullOrWhiteSpace(AssignedTo))
            {
                query = query.Where(x =>
                    x.AssignedTo != null &&
                    x.AssignedTo.Trim() == AssignedTo.Trim());
            }

            // Group by Milestone Name
            var results = await query
                .Where(x => !string.IsNullOrWhiteSpace(x.MilestoneName))
                .GroupBy(x => x.MilestoneName!)
                .Select(g => new
                {
                    MilestoneName = g.Key,
                    Count = g.Count()
                })
                .OrderBy(x => x.MilestoneName)
                .ToListAsync();

            MilestoneNames = results
                .Select(x => x.MilestoneName)
                .ToList();

            MilestoneCounts = results
                .Select(x => x.Count)
                .ToList();

            return Page();
        }
    }
}