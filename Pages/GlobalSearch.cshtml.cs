using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;

namespace QMSApplication.Pages
{
    public class GlobalSearchModel : PageModel
    {
        private readonly QMSPortalDbContext _db;

        public GlobalSearchModel(QMSPortalDbContext db)
        {
            _db = db;
        }

        public List<MDEntity> Results { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string? Query { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var access = new QMSApplication.Security.AccessService(_db);
            var actor = await access.Current(HttpContext);
            if (actor == null) return RedirectToPage("/Login");
            if (string.IsNullOrWhiteSpace(Query))
            {
                return Page();
            }

            Query = Query.Trim();

            Results = await access.Records(actor)
                .AsNoTracking()
                .Where(x =>
                    (x.MilestoneName != null &&
                     x.MilestoneName.Contains(Query)) ||

                    (x.DeliverableName != null &&
                     x.DeliverableName.Contains(Query)) ||

                    (x.AssignedTo != null &&
                     x.AssignedTo.Contains(Query)) ||

                    (x.Status != null &&
                     x.Status.Contains(Query)))
                .OrderBy(x => x.Id)
                .Take(200).ToListAsync();
            return Page();
        }
    }
}