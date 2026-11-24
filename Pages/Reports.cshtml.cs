using System.Text;
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

    [BindProperty(SupportsGet = true)] public string Section { get; set; } = "overview";
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    public string ScopeLabel { get; private set; } = "";
    public int Total { get; private set; }
    public int Completed { get; private set; }
    public int Overdue { get; private set; }
    public int FilteredCount { get; private set; }
    public int PageCount => Math.Max(1, (FilteredCount + 49) / 50);
    public int TargetCount { get; private set; }
    public int CompletedTargets { get; private set; }
    public int PendingApprovals { get; private set; }
    public int LoggedMinutes { get; private set; }
    public List<MDEntity> Records { get; private set; } = new();
    public List<WorkflowRow> Workflow { get; private set; } = new();
    public List<TimesheetRow> Timesheets { get; private set; } = new();
    public List<AuditRow> Audits { get; private set; } = new();
    public List<StatusRow> StatusSummary { get; private set; } = new();
    public string[] Sections { get; } = { "overview", "deliverables", "workflow", "timesheets", "teams", "delays", "management", "audit", "risks" };
    public List<StatusRow> WorkflowStages { get; private set; } = new();
    public List<TeamRow> TeamSummary { get; private set; } = new();
    public List<TrendRow> CompletionTrend { get; private set; } = new();
    public List<MDEntity> DelayedRecords { get; private set; } = new();
    public List<KindRow> TimeKinds { get; private set; } = new();
    public int CancelledTargets { get; private set; }
    public int ApprovedTimeEntries { get; private set; }
    public int DelayedCount { get; private set; }
    public int TotalLoggedMinutes { get; private set; }
    public string ManagementAssessment => Total == 0 ? "No deliverables match the current report filters. Review the selected scope before drawing conclusions." : Overdue > 0 ? $"Attention required: {Overdue} deliverables are overdue among {Total} in scope. Confirm delay causes, owners and recovery dates during the management review." : $"No overdue deliverables were identified among {Total} in scope. Review pending approvals and workflow completion before confirming overall project health.";
    public sealed record TeamRow(string Name, int Deliverables, int Completed, int Overdue);
    public sealed record TrendRow(string Period, int Planned, int Actual);
    public sealed record KindRow(string Name, int Minutes);

    public sealed record WorkflowRow(string Record, string Deliverable, string Language, string Target, string Assignee, string Stage, int Percent, bool Cancelled);
    public sealed record TimesheetRow(string Employee, string Kind, string Description, DateTime StartUtc, int Minutes, string Status);
    public sealed record AuditRow(DateTime AtUtc, string Actor, string Record, string Action, string Field);
    public sealed record StatusRow(string Name, int Count);

    private async Task<(PortalUser? Actor, IQueryable<MDEntity>? Query)> Authorized()
    {
        var access = new AccessService(_db);
        var actor = await access.Current(HttpContext);
        return actor == null ? (null, null) : (actor, access.Records(actor));
    }

    private IQueryable<MDEntity> Filter(IQueryable<MDEntity> query)
    {
        if (From.HasValue) query = query.Where(x => x.DeliverableTargetDate >= From.Value);
        if (To.HasValue) query = query.Where(x => x.DeliverableTargetDate <= To.Value);
        if (!string.IsNullOrWhiteSpace(Status)) query = query.Where(x => x.Status == Status);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            query = query.Where(x => x.RecordID.Contains(term) || (x.DeliverableName != null && x.DeliverableName.Contains(term)) || (x.AssignedTo != null && x.AssignedTo.Contains(term)));
        }
        return query;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var (actor, visible) = await Authorized();
        if (actor == null || visible == null) return RedirectToPage("/Login");
        if (!Sections.Contains(Section)) Section = "overview";
        if (From.HasValue && To.HasValue && From > To) { ModelState.AddModelError("", "Start date must not exceed end date."); return Page(); }
        var role = AccessService.Role(actor);
        ScopeLabel = role switch { "Admin" => "All departments", "Manager" => "Authorized teams / department", "Team Lead" => "My team", "User" => "My assigned work", _ => "Read-only deliverables" };
        var today = DateOnly.FromDateTime(DateTime.Today);
        var filtered = Filter(visible);
        Total = await filtered.CountAsync();
        Completed = await filtered.CountAsync(x => x.Status == "Completed");
        Overdue = await filtered.CountAsync(x => x.DeliverableTargetDate < today && x.Status != "Completed" && x.Status != "Cancelled");
        var statusCounts = await filtered
     .GroupBy(x => x.Status)
     .Select(g => new
     {
         Status = g.Key,
         Total = g.Count()
     })
     .OrderByDescending(x => x.Total)
     .Take(10)
     .ToListAsync();

        StatusSummary = statusCounts
            .Select(x => new StatusRow(
                x.Status ?? "Unspecified",
                x.Total
            ))
            .ToList();
        if (Section is "overview" or "deliverables" or "management")
        {
            FilteredCount = Total;
            PageNumber = Math.Clamp(PageNumber, 1, PageCount);
            Records = await filtered.OrderBy(x => x.DeliverableTargetDate).ThenBy(x => x.RecordID).Skip((PageNumber - 1) * 50).Take(50).ToListAsync();
        }
        if (Section is "overview" or "workflow" or "management")
        {
            var recordIds = filtered.Select(x => x.Id);
            var targets = _db.DeliverableTargets.AsNoTracking().Where(t => recordIds.Contains(t.Task.DeliverableId));
            if (role == "User") targets = targets.Where(t => t.AssignedUserId == actor.Id);
            TargetCount = await targets.CountAsync();
            CancelledTargets = await targets.CountAsync(t => t.IsCancelled);
            CompletedTargets = await targets.CountAsync(t => !t.IsCancelled && t.Steps.Count(s => s.Status == "Completed") == TaskWorkflow.Steps.Count);
            if (Section is "overview" or "management")
            {
                var stageCounts = await targets.Select(t => new { t.IsCancelled, Done = t.Steps.Count(s => s.Status == "Completed") }).ToListAsync();
                WorkflowStages = stageCounts.GroupBy(t => t.IsCancelled ? "Cancelled" : t.Done >= 14 ? "Complete" : t.Done == 0 ? "Not started" : t.Done <= 4 ? "Steps 1–4" : t.Done <= 8 ? "Steps 5–8" : "Steps 9–13")
                    .Select(g => new StatusRow(g.Key, g.Count())).OrderByDescending(g => g.Count).ToList();
            }
            if (Section == "workflow")
            {
                var rows = await targets.OrderBy(t => t.Task.Deliverable.RecordID).ThenBy(t => t.Task.Language).ThenBy(t => t.TargetNumber)
                    .Select(t => new { t.Task.Deliverable.RecordID, t.Task.Deliverable.DeliverableName, t.Task.Language, t.TargetNumber, t.AssignedUser.Username, t.IsCancelled, Steps = t.Steps.Select(s => new { s.StepNumber, s.Status }).ToList() })
                    .Take(500).ToListAsync();
                Workflow = rows.Select(t => {
                    var done = t.Steps.Count(s => s.Status == "Completed");
                    var next = t.Steps.Where(s => s.Status == "In Progress").OrderBy(s => s.StepNumber).FirstOrDefault()
                        ?? t.Steps.Where(s => s.Status != "Completed").OrderBy(s => s.StepNumber).FirstOrDefault();
                    var stage = t.IsCancelled ? "Cancelled" : done >= 14 ? "Completed (14/14)" : next == null ? $"{done}/14 completed" : $"Step {next.StepNumber}/14";
                    return new WorkflowRow(t.RecordID, t.DeliverableName ?? "", t.Language, t.TargetNumber, t.Username, stage, t.IsCancelled ? 0 : (int)Math.Round(done * 100m / 14), t.IsCancelled);
                }).ToList();
            }
        }
        if (Section is "timesheets" or "overview" or "teams" or "management")
        {
            // Employees can see only their own time. Managers and team leads see members of teams they manage.
            var access = new AccessService(_db);
            var entries = _db.TimeEntries.AsNoTracking().AsQueryable();
            if (role != "Admin")
            {
                if (role is "Manager" or "Team Lead")
                {
                    var teams = access.ManagedTeams(actor).Select(t => t.Id);
                    var members = _db.TeamMemberships.Where(m => teams.Contains(m.TeamId)).Select(m => m.UserId);
                    var leads = _db.Teams.Where(t => teams.Contains(t.Id)).Select(t => t.LeadUserId);
                    entries = entries.Where(e => e.UserId == actor.Id || members.Contains(e.UserId) || leads.Contains(e.UserId));
                }
                else entries = entries.Where(e => e.UserId == actor.Id);
            }
            if (From.HasValue) { var start = From.Value.ToDateTime(TimeOnly.MinValue); entries = entries.Where(e => e.StartUtc >= start); }
            if (To.HasValue) { var end = To.Value.AddDays(1).ToDateTime(TimeOnly.MinValue); entries = entries.Where(e => e.StartUtc < end); }
            LoggedMinutes = await entries.Where(e => e.WorkMinutes != null).SumAsync(e => e.WorkMinutes ?? 0);
            TotalLoggedMinutes = LoggedMinutes;
            ApprovedTimeEntries = await entries.CountAsync(e => e.Status == "Approved");
            var kinds = await entries.GroupBy(e => e.Kind).Select(g => new { Kind = g.Key, Minutes = g.Sum(e => e.WorkMinutes ?? 0) }).ToListAsync();
            TimeKinds = kinds.Select(k => new KindRow(k.Kind ?? "Other", k.Minutes)).OrderByDescending(k => k.Minutes).ToList();
            PendingApprovals = await entries.CountAsync(e => e.Status == "Submitted" || e.Status == "Pending");
            Timesheets = await entries.OrderByDescending(e => e.StartUtc).Take(200)
                .Select(e => new TimesheetRow(e.User.FullName, e.Kind, e.Description, e.StartUtc, e.WorkMinutes ?? 0, e.Status)).ToListAsync();
        }
        if (Section is "overview" or "teams" or "management")
        {
            // Aggregate only the actor's authorized deliverables; team names come from the scoped records.
            var teamRows = await filtered.GroupBy(r => r.DepartmentName)
                .Select(g => new { Name = g.Key, Total = g.Count(), Done = g.Count(r => r.Status == "Completed"), Late = g.Count(r => r.DeliverableTargetDate < today && r.Status != "Completed" && r.Status != "Cancelled") }).ToListAsync();
            TeamSummary = teamRows.Select(g => new TeamRow(g.Name ?? "Unassigned", g.Total, g.Done, g.Late)).OrderByDescending(g => g.Deliverables).ToList();
        }
        if (Section == "overview")
        {
            // Actual completion is based on recorded completion dates; planned is target dates.
            var dates = await filtered.Select(r => new { r.DeliverableTargetDate, r.DeliverableActualCompletedDate }).ToListAsync();
            var todayDate = DateOnly.FromDateTime(DateTime.UtcNow);
            CompletionTrend = Enumerable.Range(0, 6).Select(i => {
                var start = todayDate.AddMonths(i - 5);
                var monthStart = new DateOnly(start.Year, start.Month, 1);
                var monthEnd = monthStart.AddMonths(1);
                return new TrendRow(monthStart.ToString("MMM yy"), dates.Count(d => d.DeliverableTargetDate >= monthStart && d.DeliverableTargetDate < monthEnd), dates.Count(d => d.DeliverableActualCompletedDate >= monthStart && d.DeliverableActualCompletedDate < monthEnd));
            }).ToList();
        }
        if (Section is "delays" or "management")
        {
            DelayedCount = await filtered.CountAsync(r => r.DeliverableTargetDate < today && r.Status != "Completed" && r.Status != "Cancelled");
            DelayedRecords = await filtered.Where(r => r.DeliverableTargetDate < today && r.Status != "Completed" && r.Status != "Cancelled")
                .OrderBy(r => r.DeliverableTargetDate).Take(200).ToListAsync();
        }
        if (Section == "audit")
        {
            // Audit entries are linked to visible deliverables. Guests see no audit history.
            if (role != "Guest")
            {
                var codes = filtered.Select(x => x.RecordID);
                var logs = _db.MDAuditLogs.AsNoTracking().Where(a => codes.Contains(a.RecordCode));
                if (From.HasValue) { var start = From.Value.ToDateTime(TimeOnly.MinValue); logs = logs.Where(a => a.TimestampUtc >= start); }
                if (To.HasValue) { var end = To.Value.AddDays(1).ToDateTime(TimeOnly.MinValue); logs = logs.Where(a => a.TimestampUtc < end); }
                Audits = await logs.OrderByDescending(a => a.TimestampUtc).Take(200)
                    .Select(a => new AuditRow(a.TimestampUtc, a.Username, a.RecordCode, a.Action, a.ChangedField)).ToListAsync();
            }
        }
        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        var (actor, visible) = await Authorized();
        if (actor == null || visible == null) return RedirectToPage("/Login");
        var role = AccessService.Role(actor);
        if (role == "Guest")
        {
            return RedirectToPage("/AccessDenied");
        }
        var query = Filter(visible);
        var csv = new StringBuilder();
        void Add(params object?[] fields) => csv.AppendLine(string.Join(",", fields.Select(f => "\"" + (f?.ToString() ?? "").Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"")));
        if (Section == "timesheets")
        {
            Add("Employee", "Type", "Description", "Start UTC", "Minutes", "Status");
            var entries = _db.TimeEntries.AsNoTracking().AsQueryable();
            if (role != "Admin")
            {
                if (role is "Manager" or "Team Lead")
                {
                    var access = new AccessService(_db);
                    var teamIds = access.ManagedTeams(actor).Select(t => t.Id);
                    var members = _db.TeamMemberships.Where(m => teamIds.Contains(m.TeamId)).Select(m => m.UserId);
                    var leads = _db.Teams.Where(t => teamIds.Contains(t.Id)).Select(t => t.LeadUserId);
                    entries = entries.Where(e => e.UserId == actor.Id || members.Contains(e.UserId) || leads.Contains(e.UserId));
                }
                else entries = entries.Where(e => e.UserId == actor.Id);
            }
            if (From.HasValue) { var start = From.Value.ToDateTime(TimeOnly.MinValue); entries = entries.Where(e => e.StartUtc >= start); }
            if (To.HasValue) { var end = To.Value.AddDays(1).ToDateTime(TimeOnly.MinValue); entries = entries.Where(e => e.StartUtc < end); }
            var rows = await entries.OrderByDescending(e => e.StartUtc).Take(10000).Select(e => new { e.User.FullName, e.Kind, e.Description, e.StartUtc, e.WorkMinutes, e.Status }).ToListAsync();
            foreach (var r in rows) Add(r.FullName, r.Kind, r.Description, r.StartUtc, r.WorkMinutes, r.Status);
        }
        else if (Section == "audit")
        {
            Add("Timestamp UTC", "Actor", "Record", "Action", "Field");
            var codes = query.Select(r => r.RecordID);
            var logs = _db.MDAuditLogs.AsNoTracking().Where(a => codes.Contains(a.RecordCode));
            if (From.HasValue) { var start = From.Value.ToDateTime(TimeOnly.MinValue); logs = logs.Where(a => a.TimestampUtc >= start); }
            if (To.HasValue) { var end = To.Value.AddDays(1).ToDateTime(TimeOnly.MinValue); logs = logs.Where(a => a.TimestampUtc < end); }
            var rows = await logs.OrderByDescending(a => a.TimestampUtc).Take(10000).ToListAsync();
            foreach (var r in rows) Add(r.TimestampUtc, r.Username, r.RecordCode, r.Action, r.ChangedField);
        }
        else if (Section == "workflow")
        {
            Add("Record", "Deliverable", "Language", "Target", "Assignee", "Completed steps", "Cancelled");
            var ids = query.Select(r => r.Id);
            var targets = _db.DeliverableTargets.AsNoTracking().Where(t => ids.Contains(t.Task.DeliverableId));
            if (role == "User") targets = targets.Where(t => t.AssignedUserId == actor.Id);
            var rows = await targets.OrderBy(t => t.Id).Take(10000).Select(t => new { t.Task.Deliverable.RecordID, t.Task.Deliverable.DeliverableName, t.Task.Language, t.TargetNumber, t.AssignedUser.Username, Done = t.Steps.Count(s => s.Status == "Completed"), t.IsCancelled }).ToListAsync();
            foreach (var r in rows) Add(r.RecordID, r.DeliverableName, r.Language, r.TargetNumber, r.Username, r.Done, r.IsCancelled);
        }
        else
        {
            Add("Record", "Deliverable", "Department", "Assigned to", "Target date", "Actual completion", "Status", "Deviation reason");
            var rows = await query.OrderBy(r => r.RecordID).Take(10000).ToListAsync();
            foreach (var r in rows) Add(r.RecordID, r.DeliverableName, r.DepartmentName, r.AssignedTo, r.DeliverableTargetDate, r.DeliverableActualCompletedDate, r.Status, r.DeviationReason);
        }
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv", $"PMT_{Section}_{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}
