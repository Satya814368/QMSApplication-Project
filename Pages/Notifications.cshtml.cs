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
        Notifications=notices.Select(n=>new NotificationViewModel{Notification=n,IsRead=statuses.Any(s=>s.NotificationId==n.Id && s.IsRead),IsAcknowledged=statuses.Any(s=>s.NotificationId==n.Id && s.IsAcknowledged)}).ToList();
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
    public async Task<IActionResult> OnPostAcknowledgeAsync(long id)
    {
        NoCache();var actor=await Actor();if(actor==null)return RedirectToPage("/Login");
        if(!await new NotificationReaderService(db).Acknowledge(actor,id))return NotFound();
        if(Request.Headers.Accept.Any(x=>x?.Contains("application/json",StringComparison.OrdinalIgnoreCase)==true))
        {
            var pending=await new NotificationReaderService(db).Pending(actor);return new JsonResult(new{count=pending.Count,notifications=pending});
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
