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
}
