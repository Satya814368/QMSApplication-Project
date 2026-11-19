using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;

namespace QMSApplication.Pages
{
    public class NotificationsModel : PageModel
    {
        private readonly QMSPortalDbContext _db;

        public NotificationsModel(QMSPortalDbContext db)
        {
            _db = db;
        }

        public List<NotificationViewModel> Notifications { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            var username = HttpContext.Session.GetString("Username");
            var userRole = HttpContext.Session.GetString("UserRole");

            if (string.IsNullOrWhiteSpace(username))
            {
                return RedirectToPage("/Login");
            }

            var notifications = await QMSApplication.Security.NotificationReportService.Relevant(_db, userRole)
                .OrderByDescending(x => x.NotificationDateUtc)
                .ToListAsync();

            var notificationIds = notifications
                .Select(x => x.Id)
                .ToList();

            var statuses = await _db.NotificationUserStatuses
                .AsNoTracking()
                .Where(x =>
                    x.Username == username &&
                    notificationIds.Contains(x.NotificationId))
                .ToListAsync();

            Notifications = notifications.Select(notification =>
            {
                var status = statuses.FirstOrDefault(
                    x => x.NotificationId == notification.Id);

                return new NotificationViewModel
                {
                    Notification = notification,
                    IsRead = status?.IsRead ?? false,
                    IsAcknowledged = status?.IsAcknowledged ?? false
                };
            }).ToList();

            return Page();
        }

        public async Task<IActionResult> OnPostAcknowledgeAsync(long id)
        {
            var username = HttpContext.Session.GetString("Username");
            var userRole = HttpContext.Session.GetString("UserRole");

            if (string.IsNullOrWhiteSpace(username))
            {
                return RedirectToPage("/Login");
            }

            var notification = await QMSApplication.Security.NotificationReportService.Relevant(_db, userRole)
                .SingleOrDefaultAsync(x => x.Id == id);

            if (notification == null)
            {
                return NotFound();
            }

            var now = DateTime.UtcNow;

            var status = await _db.NotificationUserStatuses
                .FirstOrDefaultAsync(x =>
                    x.NotificationId == id &&
                    x.Username == username);

            if (status == null)
            {
                status = new NotificationUserStatus
                {
                    NotificationId = id,
                    Username = username,
                    IsRead = true,
                    IsAcknowledged = true,
                    ReadAtUtc = now,
                    AcknowledgedAtUtc = now,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

                _db.NotificationUserStatuses.Add(status);
            }
            else
            {
                status.IsRead = true;
                status.IsAcknowledged = true;
                status.ReadAtUtc ??= now;
                status.AcknowledgedAtUtc = now;
                status.UpdatedAtUtc = now;
            }

            await _db.SaveChangesAsync();

            return RedirectToPage();
        }
    }

    public class NotificationViewModel
    {
        public Notification Notification { get; set; } = null!;

        public bool IsRead { get; set; }

        public bool IsAcknowledged { get; set; }
    }
}