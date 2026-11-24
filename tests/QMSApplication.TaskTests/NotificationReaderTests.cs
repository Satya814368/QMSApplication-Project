using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;
using QMSApplication.Security;
using Xunit;
namespace QMSApplication.TaskTests;
public class NotificationReaderTests
{
    [Fact] public async Task PreviewReadAndAcknowledgementAreSeparateAndRoleScoped()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        connection.CreateCollation("ascii_general_ci",(a,b)=>string.Compare(a,b,StringComparison.OrdinalIgnoreCase));
        await using var db=new QMSPortalDbContext(new DbContextOptionsBuilder<QMSPortalDbContext>().UseSqlite(connection).Options);await db.Database.EnsureCreatedAsync();
        var actor=new PortalUser{Username="reader",Role="User",IsActive=true,IsApproved=true};db.Add(actor);
        var notice=new Notification{Title="Public notice",Message=new string('a',190)+"\nEntire message end",IsPublished=true,TargetRole="User"};
        var draft=new Notification{Title="Draft",Message="Secret",IsPublished=false};var foreign=new Notification{Title="Admin secret",Message="Secret",TargetRole="Admin",IsPublished=true};db.AddRange(notice,draft,foreign);await db.SaveChangesAsync();
        var reader=new NotificationReaderService(db);var preview=Assert.Single(await reader.Pending(actor));Assert.Equal(161,preview.PreviewText.Length);Assert.EndsWith("…",preview.PreviewText);Assert.False(preview.IsRead);Assert.Empty(await db.NotificationUserStatuses.ToListAsync());
        Assert.Null(await reader.Read(actor,foreign.Id));Assert.Null(await reader.Read(actor,draft.Id));Assert.False(await reader.Acknowledge(actor,foreign.Id));Assert.Empty(await db.NotificationUserStatuses.ToListAsync());
        var full=await reader.Read(actor,notice.Id);Assert.Equal(notice.Message,full!.Message);var status=await db.NotificationUserStatuses.SingleAsync();Assert.True(status.IsRead);Assert.False(status.IsAcknowledged);Assert.Null(status.AcknowledgedAtUtc);Assert.True(Assert.Single(await reader.Pending(actor)).IsRead);
        Assert.True(await reader.Acknowledge(actor,notice.Id));var timestamp=status.AcknowledgedAtUtc;Assert.NotNull(timestamp);Assert.Empty(await reader.Pending(actor));
        await reader.Read(actor,notice.Id);await reader.Acknowledge(actor,notice.Id);Assert.Equal(timestamp,status.AcknowledgedAtUtc);Assert.Single(await db.NotificationUserStatuses.ToListAsync());
        actor.IsActive=false;Assert.Empty(await reader.Pending(actor));Assert.Null(await reader.Read(actor,notice.Id));Assert.False(await reader.Acknowledge(actor,notice.Id));
    }

    [Fact]
    public async Task EditedNoticeRequiresCurrentAcknowledgementAndKeepsPreviousResponse()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateCollation("ascii_general_ci", (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
        await using var db = new QMSPortalDbContext(new DbContextOptionsBuilder<QMSPortalDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var user = new PortalUser { Username = "reader", FullName = "Reader", Role = "User", IsActive = true, IsApproved = true };
        var admin = new PortalUser { Username = "admin", FullName = "Administrator", Role = "Admin", IsActive = true, IsApproved = true };
        var notice = new Notification { Title = "Original", Message = "Old message", IsPublished = true, TargetRole = "User", Revision = 0 };
        db.AddRange(user, admin, notice);
        await db.SaveChangesAsync();

        var reader = new NotificationReaderService(db);
        var report = new NotificationReportService(db);
        var original = Assert.Single(await reader.Pending(user));
        Assert.Equal(0, original.Revision);
        Assert.True(await reader.Acknowledge(user, notice.Id));
        Assert.Empty(await reader.Pending(user));

        Assert.False(notice.ApplyEdit("Original", "Old message", "User", true));
        Assert.Empty(await reader.Pending(user));
        Assert.True(notice.ApplyEdit("Edited", "New message", "User", true));
        await db.SaveChangesAsync();

        var updated = Assert.Single(await reader.Pending(user));
        Assert.Equal(1, updated.Revision);
        Assert.Equal("Edited", updated.Title);
        Assert.False(updated.IsRead);
        Assert.False(Assert.Single((await report.Build(admin, notice.Id)).Recipients).IsAcknowledged);
        Assert.Equal(NotificationReaderService.AcknowledgementResult.Updated, await reader.AcknowledgeRevision(user, notice.Id, original.Revision));
        var detail = await reader.Read(user, notice.Id);
        Assert.NotNull(detail);
        Assert.Equal("New message", detail.Message);
        Assert.Equal(1, detail.Revision);
        Assert.Equal(NotificationReaderService.AcknowledgementResult.Acknowledged, await reader.AcknowledgeRevision(user, notice.Id, detail.Revision));
        Assert.Empty(await reader.Pending(user));
        Assert.True(Assert.Single((await report.Build(admin, notice.Id)).Recipients).IsAcknowledged);
        Assert.Equal(2, await db.NotificationUserStatuses.CountAsync(s => s.NotificationId == notice.Id && s.IsAcknowledged));

        Assert.True(notice.ApplyEdit("Edited", "New message", "Admin", true));
        await db.SaveChangesAsync();
        Assert.Empty(await reader.Pending(user));
        Assert.Equal(2, Assert.Single(await reader.Pending(admin)).Revision);
    }

    [Fact]
    public void FirstPublicationIsNewAndRepublicationIsUpdated()
    {
        var notice = new Notification();
        Assert.True(notice.ApplyEdit("First draft", "Text", null, false));
        Assert.Null(notice.Revision);
        Assert.True(notice.ApplyEdit("Published", "Final text", "User", true));
        Assert.Equal(0, notice.Revision.GetValueOrDefault(-1));
        notice.SetPublication(false);
        Assert.Equal(0, notice.Revision.GetValueOrDefault(-1));
        notice.SetPublication(true);
        Assert.Equal(1, notice.Revision.GetValueOrDefault(-1));
    }
}
