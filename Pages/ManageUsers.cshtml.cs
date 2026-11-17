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
    public List<PasswordResetRequest> Requests { get; private set; } = new();
    public List<TeamAuditEvent> Audit { get; private set; } = new();
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool PendingOnly { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;
    public int Total { get; set; }

    public int Pages => Math.Max(1, (Total + 49) / 50);
    public string? ResetLink { get; set; }

    private async Task<bool> Allowed()
    {
        _actor = await _access.Current(HttpContext);
        return _actor != null && AccessService.Role(_actor) == "Admin";
    }

    private async Task Load()
    {
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var q = _db.PortalUsers.AsNoTracking();
        if (PendingOnly)
            q = q.Where(x => !x.IsApproved);
        if (!string.IsNullOrWhiteSpace(Search))
            q = q.Where(x => x.Username.Contains(Search) || x.FullName.Contains(Search));
        Total = await q.CountAsync();
        PageNumber = Math.Clamp(PageNumber, 1, Pages);
        Users = await q.OrderBy(x => x.Username).Skip((PageNumber - 1) * 50).Take(50).Select(x => new PortalUser { Id = x.Id, Username = x.Username, FullName = x.FullName, Role = x.Role, IsActive = x.IsActive, IsApproved = x.IsApproved, Version = x.Version, DepartmentId = x.DepartmentId, ManagerDepartmentScope = x.ManagerDepartmentScope }).ToListAsync();
        Departments = await _db.Departments.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        Requests = await _db.PasswordResetRequests.AsNoTracking().Include(x => x.User).Where(x => !x.IsClosed && x.TokenHash == null).OrderByDescending(x => x.RequestedAtUtc).Take(50).ToListAsync();
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
        var error = await _accounts.Change(_actor!.Username, userId, version, role ?? "", approved, active, departmentId, departmentScope, reason ?? "");
        if (error == null)
        {
            TempData["AccountSuccess"] = "Account saved. Role/team changes require a fresh sign-in; reassign team relationships after a role change.";
            return RedirectToPage();
        }

        ModelState.AddModelError("", error);
        await Load();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteUserAsync(long userId, Guid version, string? confirmation, string? reason)
    {
        if (!await Allowed()) return RedirectToPage("/AccessDenied");
        if (!ModelState.IsValid) { await Load(); return Page(); }
        var error = await _accounts.DeleteUser(_actor!.Username, userId, version, confirmation ?? "", reason ?? "");
        if (error == null)
        {
            TempData["AccountSuccess"] = "Unused account permanently deleted. Its removal is recorded in the audit trail.";
            return RedirectToPage(new { Search, PendingOnly, PageNumber });
        }
        ModelState.AddModelError("", error);
        await Load(); return Page();
    }

    public async Task<IActionResult> OnPostDeleteDepartmentAsync(long departmentId, string? confirmation, string? reason)
    {
        if (!await Allowed()) return RedirectToPage("/AccessDenied");
        if (!ModelState.IsValid) { await Load(); return Page(); }
        var error = await _accounts.DeleteDepartment(_actor!.Username, departmentId, confirmation ?? "", reason ?? "");
        if (error == null)
        {
            TempData["AccountSuccess"] = "Unused department deleted. Its removal is recorded in the audit trail.";
            return RedirectToPage(new { Search, PendingOnly, PageNumber });
        }
        ModelState.AddModelError("", error);
        await Load(); return Page();
    }

    public async Task<IActionResult> OnPostResetAsync(long userId)
    {
        if (!await Allowed())
            return RedirectToPage("/AccessDenied");
        var result = await _accounts.IssueReset(_actor!.Username, userId);
        if (result.Error != null)
            ModelState.AddModelError("", result.Error);
        else
            ResetLink = Url.Page("/ResetPassword", new { token = result.Token });
        await Load();
        return Page();
    }

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
                return RedirectToPage();
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
