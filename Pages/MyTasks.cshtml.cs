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
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrWhiteSpace(username)) return RedirectToPage("/Login");
        var actor = await _db.PortalUsers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == username && x.IsActive);
        if (actor == null) return StatusCode(403);

        // A deliverable-level assignment is distinct from an individual target assignment.
        // Only show targets owned by this user unless the whole deliverable was assigned to them.
        var records = await _db.MDRecords.AsNoTracking()
            .Where(x => x.AssignedTo == username ||
                _db.DeliverableTasks.Any(t => t.DeliverableId == x.Id &&
                    (t.AssignedUserId == actor.Id || t.Targets.Any(y => y.AssignedUserId == actor.Id))))
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync();
        var recordIds = records.Select(x => x.Id).ToList();
        var tasks = await _db.DeliverableTasks.AsNoTracking()
            .Where(t => recordIds.Contains(t.DeliverableId))
            .Include(t => t.Targets).ThenInclude(t => t.Steps)
            .AsSplitQuery().ToListAsync();

        foreach (var record in records)
        {
            var directlyAssigned = string.Equals(record.AssignedTo, username, StringComparison.OrdinalIgnoreCase);
            var relevant = tasks.Where(t => t.DeliverableId == record.Id &&
                (directlyAssigned || t.AssignedUserId == actor.Id || t.Targets.Any(y => y.AssignedUserId == actor.Id))).ToList();
            var item = new AssignedDeliverable { Record = record, DirectlyAssigned = directlyAssigned };
            foreach (var task in relevant)
            {
                item.Languages.Add(task.Language);
                foreach (var target in task.Targets.Where(y => directlyAssigned || y.AssignedUserId == actor.Id))
                    item.Targets.Add(new AssignedTarget
                    {
                        Language = task.Language,
                        Number = target.TargetNumber,
                        CompletedSteps = target.Steps.Count(s => s.Status == "Completed"),
                        Cancelled = target.IsCancelled
                    });
            }
            Deliverables.Add(item);
        }
        return Page();
    }
}
