using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;

namespace QMSApplication.Security;

public sealed class NotificationReaderService(QMSPortalDbContext db)
{
    private static bool Eligible(PortalUser actor) => actor.IsActive && actor.IsApproved && AccessService.KnownRole(actor);

    public sealed record Preview(long Id, string Title, string PreviewText, DateTime DateUtc, bool IsRead, int Revision);
    public sealed record Detail(long Id, string Title, string Message, DateTime DateUtc, int Revision);
    public enum AcknowledgementResult { Unavailable, Updated, Acknowledged }

    public static IQueryable<Notification> AwaitingAcknowledgement(QMSPortalDbContext db, string username, string? role) =>
        NotificationReportService.Relevant(db, role)
            .Where(n => !db.NotificationUserStatuses.Any(s => s.NotificationId == n.Id && s.Username == username &&
                s.Revision == (n.Revision ?? 0) && s.IsAcknowledged));

    public async Task<List<Preview>> Pending(PortalUser actor)
    {
        if (!Eligible(actor)) return new();
        var notices = await AwaitingAcknowledgement(db, actor.Username, actor.Role)
            .OrderByDescending(n => (n.Revision ?? 0) > 0 ? n.UpdatedAtUtc : n.NotificationDateUtc)
            .ThenByDescending(n => n.Id)
            .Select(n => new
            {
                n.Id, n.Title, n.Message,
                Revision = n.Revision ?? 0,
                DateUtc = (n.Revision ?? 0) > 0 ? n.UpdatedAtUtc : n.NotificationDateUtc,
                IsRead = db.NotificationUserStatuses.Any(s => s.NotificationId == n.Id && s.Username == actor.Username &&
                    s.Revision == (n.Revision ?? 0) && s.IsRead)
            }).ToListAsync();
        return notices.Select(n =>
        {
            var text = Regex.Replace(n.Message, @"\s+", " ").Trim();
            return new Preview(n.Id, n.Title, text.Length <= 160 ? text : text[..160] + "…", n.DateUtc, n.IsRead, n.Revision);
        }).ToList();
    }

    private Task<Notification?> Find(PortalUser actor, long id) => !Eligible(actor)
        ? Task.FromResult<Notification?>(null)
        : NotificationReportService.Relevant(db, actor.Role).SingleOrDefaultAsync(n => n.Id == id);

    private async Task SetStatus(PortalUser actor, long id, int revision, bool acknowledge)
    {
        var now = DateTime.UtcNow;
        var status = await db.NotificationUserStatuses.FirstOrDefaultAsync(s => s.NotificationId == id &&
            s.Username == actor.Username && s.Revision == revision);
        if (status == null)
        {
            status = new NotificationUserStatus { NotificationId = id, Username = actor.Username, CreatedAtUtc = now, Revision = revision };
            db.NotificationUserStatuses.Add(status);
        }
        status.IsRead = true;
        status.ReadAtUtc ??= now;
        status.UpdatedAtUtc = now;
        if (acknowledge)
        {
            status.IsAcknowledged = true;
            status.AcknowledgedAtUtc ??= now;
        }
        await db.SaveChangesAsync();
    }

    public async Task<Detail?> Read(PortalUser actor, long id)
    {
        var notice = await Find(actor, id);
        if (notice == null) return null;
        var revision = notice.Revision ?? 0;
        await SetStatus(actor, id, revision, false);
        return new Detail(notice.Id, notice.Title, notice.Message, revision > 0 ? notice.UpdatedAtUtc : notice.NotificationDateUtc, revision);
    }

    public async Task<AcknowledgementResult> AcknowledgeRevision(PortalUser actor, long id, int revision)
    {
        var notice = await Find(actor, id);
        if (notice == null) return AcknowledgementResult.Unavailable;
        if ((notice.Revision ?? 0) != revision) return AcknowledgementResult.Updated;
        await SetStatus(actor, id, revision, true);
        return AcknowledgementResult.Acknowledged;
    }

    public async Task<bool> Acknowledge(PortalUser actor, long id) =>
        await AcknowledgeRevision(actor, id, 0) == AcknowledgementResult.Acknowledged;
}
