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
            if (new[] { "login", "register", "verifyregistration", "forgotpassword", "verifypasswordotp", "resetpassword", "error", "privacy", "index", "index2", "accessdenied" }.Contains(page) || context.HandlerMethod?.Name?.Contains("Logout") == true)
            { await next(); return; }
            // GETs render the existing page so pending notices can appear as a modal over it.
            // Other mutations remain blocked until the recipient explicitly acknowledges every notice.
            if (HttpMethods.IsGet(context.HttpContext.Request.Method) || HttpMethods.IsHead(context.HttpContext.Request.Method))
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

            var mandatoryPending = await QMSApplication.Security.NotificationReaderService
                .AwaitingAcknowledgement(_db, username, userRole).AnyAsync();

            if (mandatoryPending)
            {
                context.Result = context.HttpContext.Request.Headers.Accept.Any(x => x?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
                    ? new JsonResult(new { error = "Acknowledge your pending notifications before continuing." }) { StatusCode = 409 }
                    : new RedirectToPageResult("/Notifications");

                return;
            }

            await next();
        }
    }
}