using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;

namespace QMSApplication.Pages
{
    public class ImportHistoryModel : PageModel
    {
        private readonly QMSPortalDbContext _db;

        public ImportHistoryModel(QMSPortalDbContext db)
        {
            _db = db;
        }

        public List<ImportBatch> ImportBatches { get; set; } = new();

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
            if (QMSApplication.Security.AccessService.Role(actor) is not ("Admin" or "Manager")) return RedirectToPage("/AccessDenied");
            var isAdmin = QMSApplication.Security.AccessService.Role(actor) == "Admin";
            ImportBatches = await _db.ImportBatches.Where(x => isAdmin || x.ImportedBy == actor.Username)
                .AsNoTracking()
                .OrderByDescending(x => x.ImportedAtUtc)
                .ToListAsync();

            return Page();
        }
    }
}