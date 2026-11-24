using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using QMSApplication.Security;
namespace QMSApplication.Pages;
public class NotificationsModel(QMSPortalDbContext db) : PageModel
{
    public List<NotificationViewModel> Notifications {get;set;}=new();
    private Task<PortalUser?> Actor()=>new AccessService(db).Current(HttpContext);
    private void NoCache()=>Response.Headers["Cache-Control"]="no-store";
    public async Task<IActionResult> OnGetAsync()
    {
        NoCache();var actor=await Actor();if(actor==null)return RedirectToPage("/Login");
        var notices=await NotificationReportService.Relevant(db,actor.Role).OrderByDescending(n=>n.NotificationDateUtc).ToListAsync();
        var ids=notices.Select(n=>n.Id).ToList();
        var statuses=await db.NotificationUserStatuses.AsNoTracking().Where(s=>s.Username==actor.Username && ids.Contains(s.NotificationId)).ToListAsync();
        var byRevision = statuses.ToLookup(s => (s.NotificationId, s.Revision));
        Notifications = notices.Select(n =>
        {
            var current = byRevision[(n.Id, n.Revision ?? 0)];
            return new NotificationViewModel { Notification = n, IsRead = current.Any(s => s.IsRead), IsAcknowledged = current.Any(s => s.IsAcknowledged) };
        }).ToList();
        return Page();
    }
    public async Task<IActionResult> OnGetPendingAsync()
    {
        NoCache();var actor=await Actor();if(actor==null)return new UnauthorizedResult();
        var pending=await new NotificationReaderService(db).Pending(actor);
        return new JsonResult(new{count=pending.Count,notifications=pending});
    }
    public async Task<IActionResult> OnPostReadAsync(long id)
    {
        NoCache();var actor=await Actor();if(actor==null)return new UnauthorizedResult();
        var detail=await new NotificationReaderService(db).Read(actor,id);
        return detail==null?NotFound():new JsonResult(detail);
    }
    public async Task<IActionResult> OnPostAcknowledgeAsync(long id, int revision = 0)
    {
        NoCache();var actor=await Actor();if(actor==null)return RedirectToPage("/Login");
        if (!ModelState.IsValid) return BadRequest();
        var reader = new NotificationReaderService(db);
        var json = Request.Headers.Accept.Any(x => x?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);
        var result = await reader.AcknowledgeRevision(actor, id, revision);
        if (result == NotificationReaderService.AcknowledgementResult.Unavailable) return NotFound();
        if (result == NotificationReaderService.AcknowledgementResult.Updated)
        {
            const string message = "This notification was updated. Review and acknowledge the latest version.";
            if (json) return new JsonResult(new { error = message }) { StatusCode = 409 };
            TempData["NotificationUpdated"] = message;
            return RedirectToPage();
        }
        if (json)
        {
            var pending = await reader.Pending(actor);
            return new JsonResult(new { count = pending.Count, notifications = pending });
        }
        return RedirectToPage();
    }
}
public class NotificationViewModel
{
    public Notification Notification {get;set;}=null!;
    public bool IsRead {get;set;}
    public bool IsAcknowledged {get;set;}
}
