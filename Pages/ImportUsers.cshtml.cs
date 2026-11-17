using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using QMSApplication.Security;

namespace QMSApplication.Pages;
public class ImportUsersModel : PageModel
{
    private readonly QMSPortalDbContext _db;
    public ImportUsersModel(QMSPortalDbContext db) => _db = db;
    [BindProperty]
    public IFormFile? ExcelFile { get; set; }

    public string? Message { get; set; }

    public async Task<IActionResult> OnGetAsync() => await Allowed() ? Page() : RedirectToPage("/AccessDenied");
    private async Task<bool> Allowed()
    {
        var user = await new AccessService(_db).Current(HttpContext);
        return user != null && AccessService.Role(user) == "Admin";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await Allowed())
            return RedirectToPage("/AccessDenied");
        if (ExcelFile == null || ExcelFile.Length == 0 || ExcelFile.Length > 5 * 1024 * 1024 || Path.GetExtension(ExcelFile.FileName).ToLowerInvariant() != ".xlsx")
        {
            Message = "Upload a .xlsx file up to 5 MB.";
            return Page();
        }

        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            using var stream = ExcelFile.OpenReadStream();
            using var book = new XLWorkbook(stream);
            var sheet = book.Worksheet(1);
            var headers = sheet.FirstRowUsed()?.CellsUsed().ToDictionary(c => c.GetString().Trim().ToLowerInvariant(), c => c.Address.ColumnNumber) ?? new();
            if (!headers.ContainsKey("username"))
            {
                Message = "A Username column is required.";
                return Page();
            }

            var count = 0;
            foreach (var row in sheet.RowsUsed().Skip(1).Take(1000))
            {
                string Cell(string n) => headers.TryGetValue(n, out var col) ? row.Cell(col).GetString().Trim() : "";
                var name = Cell("username");
                if (name.Length is < 3 or > 100 || await _db.PortalUsers.AnyAsync(x => x.Username == name) || _db.PortalUsers.Local.Any(x => x.Username == name))
                    continue;
                var full = (Cell("firstname") + " " + Cell("lastname")).Trim();
                _db.PortalUsers.Add(new PortalUser { Username = name, FullName = full.Length == 0 ? name : full[..Math.Min(200, full.Length)], Role = "User", IsActive = false, IsApproved = false, Email = Cell("email")[..Math.Min(200, Cell("email").Length)], MobileNumber = Cell("mobilenumber")[..Math.Min(20, Cell("mobilenumber").Length)] });
                count++;
            }

            _db.TeamAuditEvents.Add(new TeamAuditEvent { TimestampUtc = DateTime.UtcNow, Username = HttpContext.Session.GetString("Username")!, Action = "Pending users imported", DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { Count = count }) });
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            Message = $"{count} pending accounts imported. Approve roles in Manage Users, then issue reset links to set passwords. Excel roles/passwords are not imported.";
        }
        catch (Exception)
        {
            await tx.RollbackAsync();
            Message = "Import failed. No accounts were saved. Check headers and data.";
        }

        return Page();
    }
}
