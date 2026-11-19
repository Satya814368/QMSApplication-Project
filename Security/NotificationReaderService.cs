using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
namespace QMSApplication.Security;
public sealed class NotificationReaderService(QMSPortalDbContext db)
{
    private static bool Eligible(PortalUser actor) => actor.IsActive && actor.IsApproved && AccessService.KnownRole(actor);
    public sealed record Preview(long Id,string Title,string PreviewText,DateTime DateUtc,bool IsRead);
    public sealed record Detail(long Id,string Title,string Message,DateTime DateUtc);
    public async Task<List<Preview>> Pending(PortalUser actor)
    {
        if(!Eligible(actor))return new();
        var notices=await NotificationReportService.Relevant(db,actor.Role)
            .Where(n=>!db.NotificationUserStatuses.Any(s=>s.NotificationId==n.Id && s.Username==actor.Username && s.IsAcknowledged))
            .OrderByDescending(n=>n.NotificationDateUtc).ThenByDescending(n=>n.Id).ToListAsync();
        var ids=notices.Select(n=>n.Id).ToList();
        var readIds=await db.NotificationUserStatuses.Where(s=>s.Username==actor.Username && s.IsRead && ids.Contains(s.NotificationId)).Select(s=>s.NotificationId).ToListAsync();
        return notices.Select(n=> {var text=Regex.Replace(n.Message,@"\s+"," ").Trim();return new Preview(n.Id,n.Title,text.Length<=160?text:text[..160]+"…",n.NotificationDateUtc,readIds.Contains(n.Id));}).ToList();
    }
    private async Task<Notification?> Find(PortalUser actor,long id) => !Eligible(actor)?null:await NotificationReportService.Relevant(db,actor.Role).SingleOrDefaultAsync(n=>n.Id==id);
    private async Task SetStatus(PortalUser actor,long id,bool acknowledge)
    {
        var now=DateTime.UtcNow;
        var status=await db.NotificationUserStatuses.FirstOrDefaultAsync(s=>s.NotificationId==id && s.Username==actor.Username);
        if(status==null){status=new NotificationUserStatus{NotificationId=id,Username=actor.Username,CreatedAtUtc=now};db.NotificationUserStatuses.Add(status);}
        status.IsRead=true;status.ReadAtUtc??=now;status.UpdatedAtUtc=now;
        if(acknowledge){status.IsAcknowledged=true;status.AcknowledgedAtUtc??=now;}
        await db.SaveChangesAsync();
    }
    public async Task<Detail?> Read(PortalUser actor,long id)
    {
        var notice=await Find(actor,id);if(notice==null)return null;
        await SetStatus(actor,id,false);return new(notice.Id,notice.Title,notice.Message,notice.NotificationDateUtc);
    }
    public async Task<bool> Acknowledge(PortalUser actor,long id)
    {
        if(await Find(actor,id)==null)return false;
        await SetStatus(actor,id,true);return true;
    }
}
