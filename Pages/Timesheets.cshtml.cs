using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using QMSApplication.Security;
using QMSApplication.Data;
namespace QMSApplication.Pages;
public class TimesheetsModel : PageModel
{
    private readonly QMSPortalDbContext db;
    private PortalUser actor = null!;
    public TimesheetsModel(QMSPortalDbContext context) => db = context;
    public bool IsManager { get; private set; }
    public bool CanReview { get; private set; }
    public List<TimeEntry> Entries { get; private set; } = new();
    public List<DeliverableTarget> Targets { get; private set; } = new();
    public List<PortalUser> Employees { get; private set; } = new();
    public TimeEntry? Active => Entries.FirstOrDefault(e => e.UserId == actor.Id && e.EndUtc == null);
    public long MyId => actor.Id;
    public string RoleName => TeamSetupService.Role(actor.Role);
    public string? Notice => TempData["TimeNotice"] as string;
    public static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), IndiaZone);
    private static readonly TimeZoneInfo IndiaZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
    private static int WorkMinutes(DateTime fromUtc, DateTime toUtc)
    {
        var from = Local(fromUtc); var to = Local(toUtc);
        if (to <= from) return 0;
        var minutes = 0;
        for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
        {
            var a = new[] { (day.AddHours(9), day.AddHours(13)), (day.AddHours(14), day.AddHours(18)) };
            foreach (var (start, end) in a)
            {
                var overlap = Math.Min(to.Ticks, end.Ticks) - Math.Max(from.Ticks, start.Ticks);
                if (overlap > 0) minutes += (int)(overlap / TimeSpan.TicksPerMinute);
            }
        }
        return minutes;
    }
    private IQueryable<PortalUser> ScopedUsers()
    {
        var q = db.PortalUsers.Where(u => u.IsActive && u.IsApproved);
        if (RoleName == "Admin") return q;
        if (RoleName == "Manager")
        {
            var teams = new AccessService(db).ManagedTeams(actor).Select(t => t.Id);
            return q.Where(u => u.Id == actor.Id || (actor.DepartmentId != null && u.DepartmentId == actor.DepartmentId && actor.ManagerDepartmentScope) || db.TeamMemberships.Any(m => m.UserId == u.Id && teams.Contains(m.TeamId)) || db.Teams.Any(t => t.LeadUserId == u.Id && teams.Contains(t.Id)));
        }
        if (RoleName == "Team Lead")
        {
            var teams = new AccessService(db).ManagedTeams(actor).Select(t => t.Id);
            return q.Where(u => u.Id == actor.Id || db.TeamMemberships.Any(m => m.UserId == u.Id && teams.Contains(m.TeamId)));
        }
        return q.Where(u => u.Id == actor.Id);
    }
    private async Task<IActionResult?> Prepare()
    {
        var found = await new AccessService(db).Current(HttpContext);
        if (found == null) return RedirectToPage("/Login");
        actor = found;
        if (RoleName == "Guest") return RedirectToPage("/AccessDenied");
        IsManager = RoleName is "Admin" or "Manager";
        CanReview = RoleName is "Admin" or "Manager" or "Team Lead";
        Employees = await ScopedUsers().AsNoTracking().OrderBy(x => x.FullName).ToListAsync();
        var ids = Employees.Select(x => x.Id).ToArray();
        Entries = await db.TimeEntries.AsNoTracking().Include(x => x.User).Include(x => x.Target).ThenInclude(x => x!.Task)
            .Where(x => ids.Contains(x.UserId)).OrderByDescending(x => x.StartUtc).Take(250).ToListAsync();
        Targets = await db.DeliverableTargets.AsNoTracking().Include(x => x.Task)
            .Where(x => x.AssignedUserId == actor.Id && !x.IsCancelled).OrderBy(x => x.TargetNumber).ToListAsync();
        return null;
    }
    public async Task<IActionResult> OnGetAsync() => await Prepare() ?? Page();
    private async Task<IActionResult> Update(Func<Task<string?>> action)
    {
        var denied = await Prepare(); if (denied != null) return denied;
        var error = await action();
        TempData["TimeNotice"] = error ?? "Saved successfully.";
        return RedirectToPage();
    }
    private void Audit(TimeEntry entry, string action, string details = "") => db.TimeEntryAudits.Add(new TimeEntryAudit { EntryId = entry.Id, ActorId = actor.Id, Action = action, Details = details, AtUtc = DateTime.UtcNow });
    public Task<IActionResult> OnPostStartAsync(long targetId) => Update(async () =>
    {
        if (await db.TimeEntries.AnyAsync(x => x.UserId == actor.Id && x.EndUtc == null)) return "End your current timer first.";
        if (!await db.DeliverableTargets.AnyAsync(x => x.Id == targetId && x.AssignedUserId == actor.Id && !x.IsCancelled)) return "Task is not assigned to you.";
        var now = DateTime.UtcNow; var local = Local(now).TimeOfDay;
        if (local < TimeSpan.FromHours(9) || local >= TimeSpan.FromHours(18) || (local >= TimeSpan.FromHours(13) && local < TimeSpan.FromHours(14))) return "Start time must be within office working hours, excluding lunch.";
        var entry = new TimeEntry { UserId = actor.Id, TargetId = targetId, Kind = "Task", StartUtc = now, CreatedUtc = now, CreatedById = actor.Id };
        db.TimeEntries.Add(entry); await db.SaveChangesAsync(); Audit(entry, "Start"); await db.SaveChangesAsync(); return null;
    });
    public Task<IActionResult> OnPostStopAsync(long entryId) => Update(async () =>
    {
        var entry = await db.TimeEntries.SingleOrDefaultAsync(x => x.Id == entryId && x.UserId == actor.Id && x.Kind == "Task" && x.EndUtc == null);
        if (entry == null) return "Active timer not found.";
        var stoppedAt = DateTime.UtcNow;
        if (await db.TimeEntries.AnyAsync(x => x.Id != entry.Id && x.UserId == actor.Id &&
            x.StartUtc < stoppedAt && (x.EndUtc == null || x.EndUtc > entry.StartUtc)))
            return "This timer overlaps another time entry. Contact your manager to resolve it.";
        entry.EndUtc = stoppedAt; entry.WorkMinutes = WorkMinutes(entry.StartUtc, stoppedAt);
        Audit(entry, "Stop", $"Recorded minutes: {entry.WorkMinutes}"); await db.SaveChangesAsync(); return null;
    });
    public Task<IActionResult> OnPostMeetingAsync(long userId, string kind, string description, DateTime day, string start, string end) => Update(async () =>
    {
        if (!IsManager) return "Only managers can add meetings.";
        if (!await ScopedUsers().AnyAsync(x => x.Id == userId)) return "Employee is outside your scope.";
        if (kind is not ("Meeting" or "Training" or "Session")) return "Invalid session type.";
        if (!TimeSpan.TryParse(start, out var a) || !TimeSpan.TryParse(end, out var b) || a < TimeSpan.FromHours(9) || b > TimeSpan.FromHours(18) || b <= a || day.Date > Local(DateTime.UtcNow).Date) return "Invalid meeting date or office hours.";
        var localStart = day.Date.Add(a); var localEnd = day.Date.Add(b);
        var utcStart = TimeZoneInfo.ConvertTimeToUtc(localStart, IndiaZone); var utcEnd = TimeZoneInfo.ConvertTimeToUtc(localEnd, IndiaZone);
        var minutes = WorkMinutes(utcStart, utcEnd); if (minutes == 0) return "No working minutes in the selected period.";
        if (await db.TimeEntries.AnyAsync(x => x.UserId == userId && x.StartUtc < utcEnd && (x.EndUtc == null || x.EndUtc > utcStart))) return "Time overlaps an existing entry. Resolve the overlap first.";
        var entry = new TimeEntry { UserId = userId, Kind = kind, Description = (description ?? "").Trim()[..Math.Min((description ?? "").Trim().Length, 120)], StartUtc = utcStart, EndUtc = utcEnd, WorkMinutes = minutes, CreatedUtc = DateTime.UtcNow, CreatedById = actor.Id };
        db.TimeEntries.Add(entry); await db.SaveChangesAsync(); Audit(entry, "ManagerMeeting"); await db.SaveChangesAsync(); return null;
    });
    public Task<IActionResult> OnPostSubmitAsync(long entryId) => Update(async () =>
    {
        var e = await db.TimeEntries.SingleOrDefaultAsync(x => x.Id == entryId && x.UserId == actor.Id && x.EndUtc != null && x.Status == "Draft");
        if (e == null) return "Only completed draft entries can be submitted.";
        e.Status = "Submitted"; Audit(e, "Submit"); await db.SaveChangesAsync(); return null;
    });
    public Task<IActionResult> OnPostReviewAsync(long entryId, bool approve, string? comment) => Update(async () =>
    {
        if (!CanReview) return "You do not have approval permission.";
        var e = await db.TimeEntries.SingleOrDefaultAsync(x => x.Id == entryId && x.Status == "Submitted" && x.EndUtc != null);
        if (e == null || e.UserId == actor.Id || !await ScopedUsers().AnyAsync(x => x.Id == e.UserId)) return "Timesheet not available for approval.";
        e.Status = approve ? "Approved" : "Rejected"; e.ReviewedUtc = DateTime.UtcNow; e.ReviewedById = actor.Id; e.ReviewComment = (comment ?? "")[..Math.Min((comment ?? "").Length, 500)];
        Audit(e, approve ? "Approve" : "Reject", e.ReviewComment); await db.SaveChangesAsync(); return null;
    });
}
