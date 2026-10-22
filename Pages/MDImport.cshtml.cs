using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QMSApplication.Data;

namespace QMSApplication.Pages
{
    public class MDImportModel : PageModel
    {
        private readonly MDExcelImporter _importer;
        public string Message { get; set; } = "";
        public bool IsSuccess { get; set; }

        [BindProperty]
        public IFormFile? ExcelFile { get; set; }

        public MDImportModel(MDExcelImporter importer) => _importer = importer;

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync()
        {
            if (ExcelFile == null || ExcelFile.Length == 0)
            {
                Message = "Please select an Excel (.xlsx) file.";
                return Page();
            }

            if (!string.Equals(Path.GetExtension(ExcelFile.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                Message = "Only .xlsx Excel files are supported. Please save your spreadsheet as .xlsx and try again.";
                return Page();
            }

            if (ExcelFile.Length > 20 * 1024 * 1024)
            {
                Message = "The file is larger than 20 MB. Please upload a smaller file.";
                return Page();
            }

            try
            {
                await using var stream = ExcelFile.OpenReadStream();
                int importedCount = await _importer.ImportAsync(stream, Path.GetFileName(ExcelFile.FileName));
                Message = $"Bulk upload completed. {importedCount} records were imported.";
                IsSuccess = true;
            }
            catch (Exception ex)
            {
                Message = $"Bulk upload failed: {ex.Message}";
            }

            return Page();
        }
    }
}
