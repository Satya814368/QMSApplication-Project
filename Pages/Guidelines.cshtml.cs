using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using QMSApplication.Security;
namespace QMSApplication.Pages;
public class GuidelinesModel(QMSPortalDbContext db) : PageModel
{
    public List<Guideline> Guidelines { get; set; } = new();
    public bool CanManage { get; private set; }
    [BindProperty] public EditorInput Input { get; set; } = new();
    public class EditorInput
    {
        public long? Id { get; set; }
        public Guid Version { get; set; }
        [Required, StringLength(200)] public string Title { get; set; } = "";
        [Required, StringLength(20000)] public string Content { get; set; } = "";
        [Range(0,10000)] public int DisplayOrder { get; set; }
        public int[] TargetRoles { get; set; } = Array.Empty<int>();
    }
    private Task<PortalUser?> Actor() => new AccessService(db).Current(HttpContext);
    private async Task Load(PortalUser actor, bool markRead)
    {
        CanManage = GuidelineService.CanManage(actor);
        Response.Headers["Cache-Control"] = "no-store";
        if (markRead) Guidelines = await new GuidelineService(db).Open(actor);
        else
        {
            var mask = GuidelineService.RoleMask(actor);
            Guidelines = await db.Guidelines.AsNoTracking().Where(g => g.IsActive && (CanManage || (g.TargetRoles & mask) != 0))
                .OrderBy(g => g.DisplayOrder).ThenBy(g => g.Id).ToListAsync();
        }
    }
    public async Task<IActionResult> OnGetAsync(long? editId = null)
    {
        var actor = await Actor(); if (actor == null) return RedirectToPage("/Login");
        if (editId.HasValue && !GuidelineService.CanManage(actor)) return RedirectToPage("/AccessDenied");
        await Load(actor, true);
        if (editId.HasValue)
        {
            if (!CanManage) return RedirectToPage("/AccessDenied");
            var g = Guidelines.FirstOrDefault(g => g.Id == editId);
            if (g == null) { TempData["GuidelineMessage"] = "Guideline is no longer available."; return RedirectToPage(); }
            Input = new() { Id = g.Id, Version = g.Version, Title = g.Title, Content = GuidelineService.DisplayContent(g), DisplayOrder = g.DisplayOrder,
                TargetRoles = Enumerable.Range(0,5).Select(i => 1 << i).Where(bit => (g.TargetRoles & bit) != 0).ToArray() };
        }
        else Input.TargetRoles = new[] { 1,2,4,8,16 };
        return Page();
    }
    public async Task<IActionResult> OnGetUnreadAsync()
    {
        Response.Headers["Cache-Control"] = "no-store";
        var actor = await Actor();
        return actor == null ? new UnauthorizedResult() : new JsonResult(new { unread = await new GuidelineService(db).HasUnread(actor) });
    }
    public async Task<IActionResult> OnPostSaveAsync()
    {
        var actor = await Actor(); if (actor == null) return RedirectToPage("/Login");
        if (!GuidelineService.CanManage(actor)) return RedirectToPage("/AccessDenied");
        if (Input.TargetRoles == null || Input.TargetRoles.Length == 0 || Input.TargetRoles.Any(x => !new[] {1,2,4,8,16}.Contains(x)))
            ModelState.AddModelError("Input.TargetRoles", "Select at least one valid target role.");
        if (ModelState.IsValid)
        {
            var mask = Input.TargetRoles!.Distinct().Aggregate(0, (a,b) => a | b);
            var error = await new GuidelineService(db).Save(actor, Input.Id, Input.Version, Input.Title, Input.Content, Input.DisplayOrder, mask);
            if (error == null) { TempData["GuidelineMessage"] = "Guideline saved. Targeted roles will see an unread indicator."; return RedirectToPage(); }
            ModelState.AddModelError("", error);
        }
        await Load(actor, false); return Page();
    }
    public async Task<IActionResult> OnPostDeleteAsync(long id, Guid version)
    {
        var actor = await Actor(); if (actor == null) return RedirectToPage("/Login");
        if (!GuidelineService.CanManage(actor)) return RedirectToPage("/AccessDenied");
        TempData["GuidelineMessage"] = await new GuidelineService(db).Delete(actor, id, version) ?? "Guideline deleted.";
        return RedirectToPage();
    }
}
