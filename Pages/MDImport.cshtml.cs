using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QMSApplication.Data;

namespace QMSApplication.Pages
{
    public class MDImportModel : PageModel
    {
        private readonly MDExcelImporter _importer;
        private readonly QMSPortalDbContext _db;

        public List<Team> AvailableTeams { get; set; } = new();
        [BindProperty] public long? TeamId { get; set; }
        public string Message { get; set; } = "";
        [BindProperty] public bool HistoricalImport { get; set; } = true;
        [BindProperty]
        public IFormFile? ExcelFile { get; set; }

        public MDImportModel(MDExcelImporter importer, QMSPortalDbContext db)
        {
            _importer = importer;
            _db = db;
        }

        private async Task<IActionResult?> CheckAccessAsync()
        {
            var access = new QMSApplication.Security.AccessService(_db);
            var actor = await access.Current(HttpContext);
            if (actor == null) return RedirectToPage("/Login");
            if (QMSApplication.Security.AccessService.Role(actor) is not ("Admin" or "Manager")) return RedirectToPage("/AccessDenied");
            AvailableTeams = await access.ManagedTeams(actor).OrderBy(x => x.Name).ToListAsync();
            return null;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var denied = await CheckAccessAsync();
            return denied ?? Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var denied = await CheckAccessAsync();
            if (denied != null) return denied;
            if (!AvailableTeams.Any(x => x.Id == TeamId)) { Message = "Choose a team you manage."; return Page(); }
            if (ExcelFile != null && ExcelFile.Length > 20 * 1024 * 1024) { Message = "Use a file no larger than 20 MB."; return Page(); }
            if (ExcelFile == null || ExcelFile.Length == 0)
            {
                Message = "Please select a valid Excel file.";
                return Page();
            }

            if (!Path.GetExtension(ExcelFile.FileName)
                .Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                Message = "Only .xlsx Excel files are allowed.";
                return Page();
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                int importedCount =
                    await _importer.ImportAsync(ExcelFile, TeamId!.Value, HttpContext.Session.GetString("Username")!, HistoricalImport);
                await transaction.CommitAsync();

                Message =
                    $"Import completed successfully. {importedCount} M&D records were imported into MySQL.";
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Message = ex is InvalidOperationException ? ex.Message : "Import failed. No records were saved; check the file or contact Admin.";
            }

            return Page();
        }

    }
}