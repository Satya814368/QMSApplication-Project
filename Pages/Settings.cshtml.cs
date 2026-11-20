using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using QMSApplication.Security;
using QMSApplication.Data;
using System.ComponentModel.DataAnnotations;

namespace QMSApplication.Pages;
public class SettingsModel : PageModel
{
    private readonly QMSPortalDbContext db;
    public SettingsModel(QMSPortalDbContext db) => this.db = db;
    public PortalUser CurrentUser { get; private set; } = null!;
    public UserPreference Preferences { get; private set; } = new();
    public Dictionary<string,string> Workspace { get; private set; } = new();
    public List<SettingsAuditEvent> AuditEvents { get; private set; } = new();
    public bool IsAdmin => TeamSetupService.Role(CurrentUser.Role) == "Admin";
    public bool IsManager => TeamSetupService.Role(CurrentUser.Role) == "Manager";
    [TempData] public string? Message { get; set; }
    public string Section { get; private set; } = "profile";
    public static readonly string[] Sections = { "profile", "security", "notifications", "appearance", "schedule", "timesheets", "project", "access", "audit" };
    public string Setting(string key, string fallback) => Workspace.TryGetValue(key, out var v) ? v : fallback;
    private async Task<IActionResult?> Load(string? section)
    {
        var u = await new AccessService(db).Current(HttpContext);
        if (u == null) return RedirectToPage("/Login");
        CurrentUser = u;
        Section = Sections.Contains(section ?? "") ? section! : "profile";
        Preferences = await db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == u.Id) ?? new UserPreference { UserId = u.Id };
        Workspace = await db.WorkspaceSettings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value);
        if (Section == "audit" && IsAdmin) AuditEvents = await db.SettingsAuditEvents.AsNoTracking().OrderByDescending(x => x.AtUtc).Take(75).ToListAsync();
        return null;
    }
    public async Task<IActionResult> OnGetAsync(string? section) => await Load(section) ?? Page();
    private async Task<IActionResult> Finish(string section, Func<Task<string>> save)
    {
        var denied = await Load(section); if (denied != null) return denied;
        Message = await save();
        return RedirectToPage(new { section });
    }
    private void Audit(string key, string? oldValue, string? newValue) => db.SettingsAuditEvents.Add(new SettingsAuditEvent {
        ActorId = CurrentUser.Id, SettingKey = key, PreviousValue = oldValue, NewValue = newValue, AtUtc = DateTime.UtcNow
    });
    public Task<IActionResult> OnPostProfileAsync(string fullName, string? mobileNumber) => Finish("profile", async () => {
        fullName = (fullName ?? "").Trim(); mobileNumber = (mobileNumber ?? "").Trim();
        if (fullName.Length is < 2 or > 200 || mobileNumber.Length > 20) return "Please enter a valid name and phone number.";
        var user = await db.PortalUsers.SingleAsync(x => x.Id == CurrentUser.Id);
        if (user.FullName != fullName) Audit("Profile.FullName", user.FullName, fullName);
        if (user.MobileNumber != mobileNumber) Audit("Profile.Mobile", user.MobileNumber, mobileNumber);
        user.FullName = fullName; user.MobileNumber = mobileNumber;
        await db.SaveChangesAsync(); HttpContext.Session.SetString("LoggedInUser", fullName);
        return "Profile saved.";
    });
    public Task<IActionResult> OnPostPreferencesAsync(string theme, string language, string dateFormat, bool taskAlerts, bool approvalAlerts, bool emailAlerts) => Finish("appearance", async () => {
        if (!new[]{"Light","Dark","System"}.Contains(theme) || !new[]{"English"}.Contains(language) || !new[]{"dd-MM-yyyy","MM/dd/yyyy","yyyy-MM-dd"}.Contains(dateFormat)) return "Invalid preference selection.";
        var pref = await db.UserPreferences.FirstOrDefaultAsync(x => x.UserId == CurrentUser.Id);
        if (pref == null) { pref = new UserPreference { UserId = CurrentUser.Id }; db.UserPreferences.Add(pref); }
        var before = $"{pref.Theme}|{pref.Language}|{pref.DateFormat}|{pref.TaskAlerts}|{pref.ApprovalAlerts}|{pref.EmailAlerts}";
        pref.Theme = theme; pref.Language = language; pref.DateFormat = dateFormat; pref.TaskAlerts = taskAlerts; pref.ApprovalAlerts = approvalAlerts; pref.EmailAlerts = emailAlerts;
        Audit("UserPreferences", before, $"{theme}|{language}|{dateFormat}|{taskAlerts}|{approvalAlerts}|{emailAlerts}");
        await db.SaveChangesAsync(); return "Preferences saved. Notification delivery and theme application require integration with their respective modules.";
    });
    private async Task<string> SaveAdminSettings(string prefix, Dictionary<string,string> changes)
    {
        if (!IsAdmin) return "Only Admin can change organization settings.";
        foreach (var pair in changes) {
            var key = prefix + "." + pair.Key;
            var setting = await db.WorkspaceSettings.FindAsync(key);
            if (setting == null) { setting = new WorkspaceSetting { Key = key }; db.WorkspaceSettings.Add(setting); }
            if (setting.Value == pair.Value) continue;
            Audit(key, setting.Value, pair.Value);
            setting.Value = pair.Value; setting.UpdatedById = CurrentUser.Id; setting.UpdatedUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(); return "Settings saved. Existing Timesheets calculations still use their current fixed office-hours rules until explicitly integrated.";
    }
    public Task<IActionResult> OnPostScheduleAsync(string officeStart, string officeEnd, string lunchStart, string lunchEnd, string timezone) => Finish("schedule", async () => {
        if (!TimeSpan.TryParse(officeStart, out var a) || !TimeSpan.TryParse(officeEnd, out var b) || !TimeSpan.TryParse(lunchStart, out var c) || !TimeSpan.TryParse(lunchEnd, out var d) || !(a < c && c < d && d < b) || b-a > TimeSpan.FromHours(24) || timezone != "Asia/Kolkata") return "Invalid schedule.";
        return await SaveAdminSettings("Schedule", new() { ["OfficeStart"] = officeStart, ["OfficeEnd"] = officeEnd, ["LunchStart"] = lunchStart, ["LunchEnd"] = lunchEnd, ["Timezone"] = timezone });
    });
    public Task<IActionResult> OnPostTimesheetRulesAsync(bool requireApproval, bool preventOverlaps, int submissionDay) => Finish("timesheets", async () => {
        if (submissionDay is < 1 or > 31) return "Invalid submission day.";
        return await SaveAdminSettings("Timesheets", new() { ["RequireApproval"] = requireApproval.ToString(), ["PreventOverlaps"] = preventOverlaps.ToString(), ["SubmissionDay"] = submissionDay.ToString() });
    });
    public Task<IActionResult> OnPostProjectAsync(string projectName, string? description) => Finish("project", async () => {
        projectName = (projectName ?? "").Trim(); description = (description ?? "").Trim();
        if (projectName.Length is < 3 or > 100 || description.Length > 500) return "Project name must be 3–100 characters; description at most 500.";
        return await SaveAdminSettings("Project", new() { ["Name"] = projectName, ["Description"] = description });
    });
}
