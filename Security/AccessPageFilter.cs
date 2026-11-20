using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using QMSApplication.Data;

namespace QMSApplication.Security;
public sealed class AccessPageFilter : IAsyncPageFilter
{
    private readonly AccessService _access;
    public AccessPageFilter(AccessService access) => _access = access;
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;
    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var page = context.ActionDescriptor.ViewEnginePath.Trim('/').ToLowerInvariant();
        if (new[]
        {
            "login",
            "register", "verifyregistration",
            "forgotpassword", "verifypasswordotp",
            "resetpassword",
            "error",
            "privacy",
            "index",
            "index2"
        }.Contains(page))
        {
            await next();
            return;
        }

        var user = await _access.Current(context.HttpContext);
        if (user == null)
        {
            context.Result = new RedirectToPageResult("/Login");
            return;
        }

        var role = AccessService.Role(user);
        var denied = (new[]
        {
            "manageusers",
            "importusers"
        }.Contains(page) && role != "Admin") || (new[]
        {
            "mdcreate",
            "mdedit"
        }.Contains(page) && !AccessService.CanCreate(user)) || (page == "mdimport" && role is not ("Admin" or "Manager")) || (page == "importhistory" && role is not ("Admin" or "Manager")) || (new[]
        {
            "notificationcreate",
            "notificationedit"
        }.Contains(page) && role is not ("Admin" or "Manager"));
        if (page == "notificationacknowledgements" && role is not ("Admin" or "Manager" or "Team Lead")) denied = true;
        if (page == "notificationadmin" && role is not ("Admin" or "Manager" or "Team Lead")) denied = true;
        var handler = context.HandlerMethod?.Name ?? "";
        if (role == "Guest" && HttpMethods.IsPost(context.HttpContext.Request.Method) && !handler.Contains("Logout") && page != "changepassword" && !(page == "notifications" && (handler.Contains("Acknowledge") || handler.Contains("Read"))))
            denied = true;
        if (denied)
        {
            context.Result = new RedirectToPageResult("/AccessDenied");
            return;
        }

        await next();
    }
}
