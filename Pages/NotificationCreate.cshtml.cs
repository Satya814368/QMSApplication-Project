using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QMSApplication.Models;

namespace QMSApplication.Pages
{
    public class NotificationCreateModel : PageModel
    {
        private readonly QMSPortalDbContext _db;

        public NotificationCreateModel(QMSPortalDbContext db)
        {
            _db = db;
        }

        [BindProperty]
        public string Title { get; set; } = string.Empty;

        [BindProperty]
        public string Message { get; set; } = string.Empty;

        

        [BindProperty]
        public bool IsPublished { get; set; }

        [BindProperty]
        public string? TargetRole { get; set; }

        private bool IsAdminOrManager()
        {
            var role = HttpContext.Session.GetString("UserRole");

            return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase);
        }

        public IActionResult OnGet()
        {
            if (!IsAdminOrManager())
            {
                return RedirectToPage("/Notifications");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!IsAdminOrManager())
            {
                return RedirectToPage("/Notifications");
            }

            if (string.IsNullOrWhiteSpace(Title))
            {
                ModelState.AddModelError(
                    nameof(Title),
                    "Title is required.");
            }

            if (string.IsNullOrWhiteSpace(Message))
            {
                ModelState.AddModelError(
                    nameof(Message),
                    "Message is required.");
            }

if (!string.IsNullOrWhiteSpace(TargetRole) && !QMSApplication.Security.NotificationReportService.TargetRoles.Contains(TargetRole))
    ModelState.AddModelError(nameof(TargetRole), "Choose a valid target role.");

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var now = DateTime.UtcNow;

            var notification = new Notification
            {
                Title = Title.Trim(),
                Message = Message.Trim(),
                NotificationDateUtc = now,
                IsPublished = IsPublished,
                TargetRole = string.IsNullOrWhiteSpace(TargetRole)
                    ? null
                    : TargetRole,
                IsRead = false,
                IsAcknowledged = false,
                ReadAtUtc = null,
                AcknowledgedAtUtc = null,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            _db.Notifications.Add(notification);

            await _db.SaveChangesAsync();

            return RedirectToPage("/NotificationAdmin");
        }
    }
}
