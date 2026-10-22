using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QMSApplication.Data;

namespace QMSApplication.Pages
{
    public class MDImportModel : PageModel
    {
        private readonly MDExcelImporter _importer;

        public string Message { get; set; } = "";
        [BindProperty]
        public IFormFile? ExcelFile { get; set; }

        public MDImportModel(MDExcelImporter importer)
        {
            _importer = importer;
        }

        public void OnGet()
        {
        }


        public async Task<IActionResult> OnPostAsync()
        {
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

            try
            {
                int importedCount =
                    await _importer.ImportAsync(ExcelFile);

                Message =
                    $"Import completed successfully. {importedCount} M&D records were imported into MySQL.";
            }
            catch (Exception ex)
            {
                Message = $"Import failed: {ex.Message}";
            }

            return Page();
        }

    }
}