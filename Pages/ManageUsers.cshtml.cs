using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using QMSApplication.Security;

namespace QMSApplication.Pages;
public class ManageUsersModel : PageModel
{
    private readonly QMSPortalDbContext _db;
    private readonly AccessService _access;
    private readonly AccountService _accounts;
    private PortalUser? _actor;
    public ManageUsersModel(QMSPortalDbContext db, AccessService access, AccountService accounts)
    {
        _db = db;
        _access = access;
        _accounts = accounts;
    }

    public List<PortalUser> Users { get; private set; } = new();
    public List<Department> Departments { get; private set; } = new();
    public List<TeamAuditEvent> Audit { get; private set; } = new();
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool PendingOnly { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string? RoleFilter { get; set; }
    [BindProperty(SupportsGet = true)] public string? StatusFilter { get; set; }
    [BindProperty(SupportsGet = true)] public long? DepartmentFilter { get; set; }

    public const int PageSize = 25;
    public int Total { get; private set; }
    public int AccountCount { get; private set; }
    public int ActiveCount { get; private set; }
    public int PendingCount { get; private set; }
    public int InactiveCount => AccountCount - ActiveCount - PendingCount;
    public long? EditingUserId { get; private set; }
    public long? DeletingUserId { get; private set; }
    public long? DeletingDepartmentId { get; private set; }
    public Dictionary<string, string> FilterRoute(int page, string? status = null)
    {
        var route = new Dictionary<string, string> { [nameof(PageNumber)] = page.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        if (!string.IsNullOrEmpty(Search)) route[nameof(Search)] = Search;
        if (!string.IsNullOrEmpty(RoleFilter)) route[nameof(RoleFilter)] = RoleFilter;
        if (DepartmentFilter.HasValue) route[nameof(DepartmentFilter)] = DepartmentFilter.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var selectedStatus = status ?? StatusFilter;
        if (!string.IsNullOrEmpty(selectedStatus)) route[nameof(StatusFilter)] = selectedStatus;
        return route;
    }

    public int Pages => Math.Max(1, (Total + PageSize - 1) / PageSize);

    private IActionResult ReturnToDirectory() => RedirectToPage(new
    {
        Search, StatusFilter, RoleFilter, DepartmentFilter, PendingOnly, PageNumber
    });

    private async Task<bool> Allowed()
    {
        _actor = await _access.Current(HttpContext);
        return _actor != null && AccessService.Role(_actor) == "Admin";
    }

    private async Task Load()
    {
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Search = Search?.Trim();
        if (Search?.Length > 200) Search = Search[..200];
        RoleFilter = AccountService.Roles.FirstOrDefault(r => string.Equals(r, RoleFilter, StringComparison.OrdinalIgnoreCase));
        StatusFilter = PendingOnly && string.IsNullOrEmpty(StatusFilter) ? "Pending" : StatusFilter;
        StatusFilter = StatusFilter?.Trim().ToLowerInvariant() switch
        {
            "active" => "Active", "pending" => "Pending", "inactive" => "Inactive", _ => null
        };
        Departments = await _db.Departments.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        if (DepartmentFilter is < 0) DepartmentFilter = null;

        var accounts = _db.PortalUsers.AsNoTracking();
        var counts = await accounts.GroupBy(u => new { u.IsApproved, u.IsActive })
            .Select(g => new { g.Key.IsApproved, g.Key.IsActive, Count = g.Count() }).ToListAsync();
        AccountCount = counts.Sum(g => g.Count);
        ActiveCount = counts.Where(g => g.IsApproved && g.IsActive).Sum(g => g.Count);
        PendingCount = counts.Where(g => !g.IsApproved).Sum(g => g.Count);

        var q = accounts;
        if (!string.IsNullOrEmpty(Search))
        {
            var search = Search.ToLowerInvariant();
            q = q.Where(x => x.Username.ToLower().Contains(search) || x.FullName.ToLower().Contains(search)
                || (x.Email != null && x.Email.ToLower().Contains(search)));
        }
        q = StatusFilter switch
        {
            "Active" => q.Where(x => x.IsApproved && x.IsActive),
            "Pending" => q.Where(x => !x.IsApproved),
            "Inactive" => q.Where(x => x.IsApproved && !x.IsActive),
            _ => q
        };
        if (RoleFilter != null)
        {
            var names = RoleFilter switch
            {
                "User" => new[] { "user", "member" },
                "Team Lead" => new[] { "team lead", "teamlead", "team lead (tl)", "tl" },
                _ => new[] { RoleFilter.ToLowerInvariant() }
            };
            q = q.Where(x => names.Contains(x.Role.Trim().ToLower()));
        }
        if (DepartmentFilter == 0) q = q.Where(x => x.DepartmentId == null);
        else if (DepartmentFilter.HasValue) q = q.Where(x => x.DepartmentId == DepartmentFilter);
        Total = await q.CountAsync();
        PageNumber = Math.Clamp(PageNumber, 1, Pages);
        Users = await q.OrderBy(x => x.Username).ThenBy(x => x.Id).Skip((PageNumber - 1) * PageSize).Take(PageSize)
            .Select(x => new PortalUser { Id = x.Id, Username = x.Username, FullName = x.FullName, Email = x.Email, Role = x.Role,
                IsActive = x.IsActive, IsApproved = x.IsApproved, Version = x.Version, DepartmentId = x.DepartmentId,
                ManagerDepartmentScope = x.ManagerDepartmentScope }).ToListAsync();
        Audit = await _db.TeamAuditEvents.AsNoTracking().Where(x => x.Action.Contains("Account") || x.Action.Contains("Password") || x.Action.Contains("Registration") || x.Action.Contains("Department")).OrderByDescending(x => x.Id).Take(50).ToListAsync();
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await Allowed())
            return RedirectToPage("/AccessDenied");
        await Load();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(long userId, Guid version, string? role, bool approved, bool active, long? departmentId, bool departmentScope, string? reason)
    {
        if (!await Allowed())
            return RedirectToPage("/AccessDenied");
        EditingUserId = userId;
        if (!ModelState.IsValid) { await Load(); return Page(); }
        var error = await _accounts.Change(_actor!.Username, userId, version, role ?? "", approved, active, departmentId, departmentScope, reason ?? "");
        if (error == null)
        {
            TempData["AccountSuccess"] = "Account saved. Role/team changes require a fresh sign-in; reassign team relationships after a role change.";
            return ReturnToDirectory();
        }

        ModelState.AddModelError("", error);
        await Load();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteUserAsync(long userId, Guid version, string? confirmation, string? reason)
    {
        if (!await Allowed()) return RedirectToPage("/AccessDenied");
        DeletingUserId = userId;
        if (!ModelState.IsValid) { await Load(); return Page(); }
        var error = await _accounts.DeleteUser(_actor!.Username, userId, version, confirmation ?? "", reason ?? "");
        if (error == null)
        {
            TempData["AccountSuccess"] = "Unused account permanently deleted. Its removal is recorded in the audit trail.";
            return ReturnToDirectory();
        }
        ModelState.AddModelError("", error);
        await Load(); return Page();
    }

    public async Task<IActionResult> OnPostDeleteDepartmentAsync(long departmentId, string? confirmation, string? reason)
    {
        if (!await Allowed()) return RedirectToPage("/AccessDenied");
        DeletingDepartmentId = departmentId;
        if (!ModelState.IsValid) { await Load(); return Page(); }
        var error = await _accounts.DeleteDepartment(_actor!.Username, departmentId, confirmation ?? "", reason ?? "");
        if (error == null)
        {
            TempData["AccountSuccess"] = "Unused department deleted. Its removal is recorded in the audit trail.";
            return ReturnToDirectory();
        }
        ModelState.AddModelError("", error);
        await Load(); return Page();
    }

    public IActionResult OnPostReset() => BadRequest("Reset links are no longer issued. Use Forgot password and an email OTP.");

    public async Task<IActionResult> OnPostDepartmentAsync(string? name)
    {
        if (!await Allowed())
            return RedirectToPage("/AccessDenied");
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 120 || await _db.Departments.AnyAsync(x => x.NormalizedName == name.ToUpper()))
            ModelState.AddModelError("", "Enter a unique department name of 1–120 characters.");
        else
        {
            _db.Departments.Add(new Department { Name = name, NormalizedName = name.ToUpperInvariant() });
            _db.TeamAuditEvents.Add(new TeamAuditEvent { TimestampUtc = DateTime.UtcNow, Username = _actor!.Username, Action = "Department created", DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { Name = name }) });
            try
            {
                await _db.SaveChangesAsync();
                return ReturnToDirectory();
            }
            catch (DbUpdateException)
            {
                ModelState.AddModelError("", "Department changed concurrently. Refresh.");
            }
        }

        await Load();
        return Page();
    }
}
