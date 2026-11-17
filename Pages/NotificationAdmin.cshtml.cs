using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;

namespace QMSApplication.Pages
{
    public class NotificationAdminModel : PageModel
    {
        private readonly QMSPortalDbContext _db;

        public NotificationAdminModel(QMSPortalDbContext db)
        {
            _db = db;
        }

        public List<Notification> Notifications { get; set; } = new();
        public Dictionary<long, List<string>> UnacknowledgedUsernames { get; set; } = new();

        private bool IsAdminOrManager()
        {
            var role = HttpContext.Session.GetString("UserRole");

            return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!IsAdminOrManager())
            {
                return RedirectToPage("/Notifications");
            }

            Notifications = await _db.Notifications
                .AsNoTracking()
                .OrderByDescending(x => x.NotificationDateUtc)
                .ToListAsync();

            var activeUsers = await _db.PortalUsers.AsNoTracking()
                .Where(x => x.IsActive)
                .Select(x => new { x.Username, x.FullName, x.Role })
                .ToListAsync();
            var statuses = await _db.NotificationUserStatuses.AsNoTracking()
                .Where(x => Notifications.Select(n => n.Id).Contains(x.NotificationId))
                .ToListAsync();

            foreach (var notification in Notifications)
            {
                if (!notification.IsPublished || !notification.IsMandatory)
                {
                    UnacknowledgedUsernames[notification.Id] = new List<string>();
                    continue;
                }

                var recipients = string.IsNullOrWhiteSpace(notification.TargetRole)
                    ? activeUsers
                    : activeUsers.Where(user => string.Equals(
                        QMSApplication.Data.TeamSetupService.Role(user.Role),
                        QMSApplication.Data.TeamSetupService.Role(notification.TargetRole),
                        StringComparison.OrdinalIgnoreCase)).ToList();

                var acknowledged = statuses
                    .Where(status => status.NotificationId == notification.Id && status.IsAcknowledged)
                    .Select(status => status.Username)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                UnacknowledgedUsernames[notification.Id] = recipients
                    .Where(user => !acknowledged.Contains(user.Username))
                    .Select(user => $"{user.FullName} ({user.Username})")
                    .OrderBy(name => name)
                    .ToList();
            }

            return Page();
        }

        public async Task<IActionResult> OnPostDeleteAsync(long id)
        {
            if (!IsAdminOrManager())
            {
                return RedirectToPage("/Notifications");
            }

            var notification = await _db.Notifications
                .FirstOrDefaultAsync(x => x.Id == id);

            if (notification == null)
            {
                return NotFound();
            }

            _db.Notifications.Remove(notification);

            await _db.SaveChangesAsync();

            return RedirectToPage();
        }
        public async Task<IActionResult> OnPostTogglePublishAsync(long id)
        {
            if (!IsAdminOrManager())
            {
                return RedirectToPage("/Notifications");
            }

            var notification = await _db.Notifications
                .FirstOrDefaultAsync(x => x.Id == id);

            if (notification == null)
            {
                return NotFound();
            }

            notification.IsPublished = !notification.IsPublished;
            notification.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return RedirectToPage();
        }
    }
}