using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;

namespace QMSApplication.Pages;

public class MyTasksModel : PageModel
{
    private readonly QMSPortalDbContext _db;
    public MyTasksModel(QMSPortalDbContext db) => _db = db;
    public List<AssignedDeliverable> Deliverables { get; private set; } = new();
    public int TotalTargets => Deliverables.Sum(x => x.Targets.Count);
    public int TotalLanguageTasks => Deliverables.Sum(x => x.Languages.Count);

    public sealed class AssignedDeliverable
    {
        public MDEntity Record { get; set; } = null!;
        public bool DirectlyAssigned { get; set; }
        public List<string> Languages { get; set; } = new();
        public List<AssignedTarget> Targets { get; set; } = new();
    }
    public sealed class AssignedTarget
    {
        public string Language { get; set; } = "";
        public string Number { get; set; } = "";
        public int CompletedSteps { get; set; }
        public bool Cancelled { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var access = new QMSApplication.Security.AccessService(_db);
        var actor = await access.Current(HttpContext);
        if (actor == null) return RedirectToPage("/Login");
        var records = await access.Records(actor).OrderByDescending(x => x.CreatedAtUtc).ToListAsync();
        var ids = records.Select(x => x.Id).ToList();
        var targets = await access.Targets(actor).AsNoTracking().Where(x => ids.Contains(x.Task.DeliverableId))
            .Include(x => x.Task).Include(x => x.Steps).AsSplitQuery().ToListAsync();
        foreach (var record in records)
        {
            var relevant = targets.Where(x => x.Task.DeliverableId == record.Id).ToList();
            Deliverables.Add(new AssignedDeliverable { Record = record,
                DirectlyAssigned = record.AssignedTo == actor.Username || record.AssignedTo == actor.FullName,
                Languages = relevant.Select(x => x.Task.Language).Distinct().ToList(),
                Targets = relevant.Select(x => new AssignedTarget { Language = x.Task.Language, Number = x.TargetNumber,
                    CompletedSteps = x.Steps.Count(s => s.Status == "Completed"), Cancelled = x.IsCancelled }).ToList() });
        }
        return Page();
    }
}
