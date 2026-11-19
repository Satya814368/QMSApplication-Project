using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;

namespace QMSApplication.Filters
{
    public class MandatoryNotificationFilter : IAsyncPageFilter
    {
        private readonly QMSPortalDbContext _db;

        public MandatoryNotificationFilter(QMSPortalDbContext db)
        {
            _db = db;
        }

        public Task OnPageHandlerSelectionAsync(
            PageHandlerSelectedContext context)
        {
            return Task.CompletedTask;
        }

        public async Task OnPageHandlerExecutionAsync(
            PageHandlerExecutingContext context,
            PageHandlerExecutionDelegate next)
        {
            var page = context.ActionDescriptor.ViewEnginePath.Trim('/').ToLowerInvariant();
            if (new[] { "login", "register", "verifyregistration", "forgotpassword", "resetpassword", "error", "privacy", "index", "index2", "accessdenied" }.Contains(page) || context.HandlerMethod?.Name?.Contains("Logout") == true)
            { await next(); return; }
            var pagePath = context.HttpContext.Request.Path.Value;

            // Allow the Notifications page itself.
            if (pagePath != null &&
                pagePath.StartsWith(
                    "/Notifications",
                    StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var username =
                context.HttpContext.Session.GetString("Username");

            var userRole =
                context.HttpContext.Session.GetString("UserRole");

            // If nobody is logged in, do not apply the notification block.
            if (string.IsNullOrWhiteSpace(username))
            {
                await next();
                return;
            }

            var mandatoryNotifications =
                await QMSApplication.Security.NotificationReportService.Relevant(_db, userRole)
                    .Select(x => x.Id)
                    .ToListAsync();

            if (mandatoryNotifications.Count == 0)
            {
                await next();
                return;
            }

            var acknowledgedNotificationIds =
                await _db.NotificationUserStatuses
                    .AsNoTracking()
                    .Where(x =>
                        x.Username == username &&
                        x.IsAcknowledged &&
                        mandatoryNotifications.Contains(
                            x.NotificationId))
                    .Select(x => x.NotificationId)
                    .ToListAsync();

            var mandatoryPending =
                mandatoryNotifications.Any(
                    id => !acknowledgedNotificationIds.Contains(id));

            if (mandatoryPending)
            {
                context.Result =
                    new RedirectToPageResult("/Notifications");

                return;
            }

            await next();
        }
    }
}