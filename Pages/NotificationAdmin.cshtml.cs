using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using QMSApplication.Security;
namespace QMSApplication.Pages;
public class NotificationAdminModel(QMSPortalDbContext db) : PageModel
{
    public List<Notification> Notifications { get; set; } = new();
    public bool CanManage { get; private set; }
    public sealed record AcknowledgementPopup(NotificationReportService.Report Report, bool IsAdmin, long Id, long? DepartmentId, long? TeamId);
    private Task<PortalUser?> Actor() => new AccessService(db).Current(HttpContext);
    private static bool CanWrite(PortalUser? actor) => actor != null && actor.IsActive && actor.IsApproved && AccessService.Role(actor) is "Admin" or "Manager";
    public async Task<IActionResult> OnGetAsync()
    {
        Response.Headers["Cache-Control"] = "no-store";
        var actor = await Actor();
        if (actor == null) return RedirectToPage("/Login");
        if (!NotificationReportService.CanTrack(actor)) return RedirectToPage("/AccessDenied");
        CanManage = CanWrite(actor);
        Notifications = CanManage ? await db.Notifications.AsNoTracking().OrderByDescending(x => x.NotificationDateUtc).ToListAsync()
            : (await new NotificationReportService(db).Build(actor)).Notifications;
        return Page();
    }
    public async Task<IActionResult> OnGetAcknowledgementsAsync(long id, long? departmentId = null, long? teamId = null)
    {
        Response.Headers["Cache-Control"] = "no-store";
        var actor = await Actor();
        if (actor == null) return new UnauthorizedResult();
        if (!NotificationReportService.CanTrack(actor)) return StatusCode(403);
        var report = await new NotificationReportService(db).Build(actor, id, departmentId, teamId);
        if (report.Forbidden) return StatusCode(403);
        return Partial("_NotificationAcknowledgementsPopup", new AcknowledgementPopup(report,AccessService.Role(actor)=="Admin",id,departmentId,teamId));
    }
    public async Task<IActionResult> OnPostDeleteAsync(long id)
    {
        if (!CanWrite(await Actor())) return RedirectToPage("/AccessDenied");
        var notification = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id);
        if (notification == null) return NotFound();
        db.Notifications.Remove(notification); await db.SaveChangesAsync(); return RedirectToPage();
    }
    public async Task<IActionResult> OnPostTogglePublishAsync(long id)
    {
        if (!CanWrite(await Actor())) return RedirectToPage("/AccessDenied");
        var notification = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id);
        if (notification == null) return NotFound();
        notification.SetPublication(!notification.IsPublished);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            TempData["NotificationConflict"] = "This notification changed while you were working. Review it and try again.";
        }
        return RedirectToPage();
    }
}
