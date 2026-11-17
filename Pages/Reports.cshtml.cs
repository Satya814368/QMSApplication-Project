using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using QMSApplication.Security;

namespace QMSApplication.Pages;
public class ReportsModel : PageModel
{
    private readonly QMSPortalDbContext _db;
    public ReportsModel(QMSPortalDbContext db) => _db = db;
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;
    public int Total { get; set; }

    public int Completed { get; set; }

    public int Overdue { get; set; }

    public int FilteredCount { get; set; }

    public int PageCount => Math.Max(1, (FilteredCount + 49) / 50);
    public List<MDEntity> Records { get; set; } = new();
    public async Task<IActionResult> OnGetAsync()
    {
        var access = new AccessService(_db);
        var actor = await access.Current(HttpContext);
        if (actor == null)
            return RedirectToPage("/Login");
        var records = access.Records(actor);
        var today = DateOnly.FromDateTime(DateTime.Today);
        Total = await records.CountAsync();
        Completed = await records.CountAsync(x => x.Status == "Completed");
        Overdue = await records.CountAsync(x => x.DeliverableTargetDate < today && x.Status != "Completed" && x.Status != "Cancelled");
        var term = Search?.Trim();
        if (!string.IsNullOrEmpty(term))
            records = records.Where(x => x.RecordID.Contains(term) || (x.DeliverableName != null && x.DeliverableName.Contains(term)));
        FilteredCount = await records.CountAsync();
        PageNumber = Math.Clamp(PageNumber, 1, PageCount);
        Records = await records.OrderBy(x => x.RecordID).Skip((PageNumber - 1) * 50).Take(50).ToListAsync();
        return Page();
    }
}
