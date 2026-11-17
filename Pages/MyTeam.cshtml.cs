using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Data;
using QMSApplication.Models;

namespace QMSApplication.Pages;

public class MyTeamModel : PageModel
{
    private readonly QMSPortalDbContext _db;
    private readonly TeamSetupService _setup;
    private PortalUser? _actor;
    public MyTeamModel(QMSPortalDbContext db, TeamSetupService setup) { _db = db; _setup = setup; }
    public bool CanSetLeadership => new[] { "Admin", "Manager" }.Contains(TeamSetupService.Role(_actor?.Role));
    public List<Department> Departments { get; private set; } = new();
    public bool IsAdmin => TeamSetupService.Role(_actor?.Role) == "Admin";
    public List<Team> Teams { get; private set; } = new();
    public List<PortalUser> Users { get; private set; } = new();
    public List<TeamManager> Managers { get; private set; } = new();
    public Dictionary<long, long> Memberships { get; private set; } = new();
    public Dictionary<long, string> TeamNames { get; private set; } = new();
    public Dictionary<long, List<PortalUser>> TeamMembers { get; private set; } = new();
    public List<MDEntity> Records { get; private set; } = new();
    public List<TeamAuditEvent> Audits { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public bool OnlyUnassigned { get; set; } = true;
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    public int RecordCount { get; private set; }
    public int PageCount => Math.Max(1, (RecordCount + 49) / 50);
    public string UserName(long id) => Users.FirstOrDefault(x => x.Id == id)?.FullName ?? $"Inactive / unavailable #{id}";
    public bool Owns(long teamId) => Teams.Any(x => x.Id == teamId);
    private async Task<IActionResult?> Authorize()
    {
        _actor = await new QMSApplication.Security.AccessService(_db).Current(HttpContext);
        if (_actor == null || !new[] { "Admin", "Manager", "Team Lead" }.Contains(TeamSetupService.Role(_actor.Role)))
            return RedirectToPage("/AccessDenied");
        return null;
    }
    private async Task Load()
    {
        Teams = await _setup.ManagedTeams(_actor!).AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        var ids = Teams.Select(x => x.Id).ToList();
        TeamNames = await _db.Teams.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name);
        Managers = await _db.TeamManagers.AsNoTracking().Where(x => ids.Contains(x.TeamId)).ToListAsync();
        Memberships = await _db.TeamMemberships.AsNoTracking()
            .ToDictionaryAsync(x => x.UserId, x => x.TeamId);
        Users = await _db.PortalUsers.AsNoTracking()
            .Where(x => x.IsActive && x.IsApproved)
            .OrderBy(x => x.FullName)
            .Select(x => new PortalUser { Id = x.Id, Username = x.Username, FullName = x.FullName, Role = x.Role })
            .ToListAsync();
        TeamMembers = Teams.ToDictionary(team => team.Id,
            team => Users.Where(user => Memberships.TryGetValue(user.Id, out var memberTeamId) && memberTeamId == team.Id).ToList());
        if (IsAdmin) Departments = await _db.Departments.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        if (IsAdmin)
        {
            var query = _db.MDRecords.AsNoTracking().AsQueryable();
            if (OnlyUnassigned) query = query.Where(x => x.TeamId == null);
            var term = Search?.Trim();
            if (!string.IsNullOrEmpty(term)) query = query.Where(x => x.RecordID.Contains(term) || (x.DeliverableName != null && x.DeliverableName.Contains(term)));
            RecordCount = await query.CountAsync(); PageNumber = Math.Clamp(PageNumber, 1, PageCount);
            Records = await query.OrderBy(x => x.Id).Skip((PageNumber - 1) * 50).Take(50).ToListAsync();
            Audits = await _db.TeamAuditEvents.AsNoTracking().OrderByDescending(x => x.Id).Take(50).ToListAsync();
        }
    }
    public async Task<IActionResult> OnGetAsync()
    { var denied = await Authorize(); if (denied != null) return denied; await Load(); return Page(); }
    private async Task<IActionResult> Change(Func<Task<string?>> change)
    {
        var denied = await Authorize(); if (denied != null) return denied;
        if (!ModelState.IsValid) { await Load(); return Page(); }
        var error = await change();
        if (error != null) { ModelState.AddModelError("", error); await Load(); return Page(); }
        TempData["TeamSuccess"] = "Change saved.";
        return RedirectToPage(new { Search, OnlyUnassigned, PageNumber });
    }
    public Task<IActionResult> OnPostCreateAsync(string? teamName, long? departmentId) => Change(() => _setup.Create(_actor!.Username, teamName ?? "", departmentId));
    public Task<IActionResult> OnPostDepartmentAsync(long teamId, long? departmentId, Guid version) => Change(() => _setup.SetDepartment(_actor!.Username, teamId, departmentId, version));
    public Task<IActionResult> OnPostLeadAsync(long teamId, long? leadId, Guid version) => Change(() => _setup.SetLead(_actor!.Username, teamId, leadId, version));
    public Task<IActionResult> OnPostManagerAsync(long teamId, long managerId, bool remove, Guid version) => Change(() => _setup.SetManager(_actor!.Username, teamId, managerId, remove, version));
    public Task<IActionResult> OnPostMemberAsync(long memberId, long destinationId, long? expectedSourceId, string? reason) => Change(() => _setup.MoveMember(_actor!.Username, memberId, destinationId, expectedSourceId, reason));
    public Task<IActionResult> OnPostRemoveAsync(long memberId, long expectedTeamId, string? reason) => Change(() => _setup.RemoveMember(_actor!.Username, memberId, expectedTeamId, reason));
    public Task<IActionResult> OnPostMapAsync(long recordId, long? teamId, Guid version) => Change(() => _setup.MapDeliverable(_actor!.Username, recordId, teamId, version));
}
