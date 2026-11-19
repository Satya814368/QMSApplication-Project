using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Security;
namespace QMSApplication.Pages;
public class ForgotPasswordModel(PasswordRecoveryOtpService otp, ILogger<ForgotPasswordModel> logger) : PageModel
{
    [BindProperty, Required, EmailAddress, StringLength(200)] public string Email { get; set; } = "";
    public bool Ready => otp.Ready;
    public string? Error { get; set; }
    public void OnGet() { Response.Headers["Cache-Control"] = "no-store";Response.Headers["Referrer-Policy"] = "no-referrer"; }
    public async Task<IActionResult> OnPostAsync()
    {
        OnGet();
        if (!Ready) { Error = "Password recovery email is not configured. Contact the administrator.";return Page(); }
        if (!ModelState.IsValid) return Page();
        try
        {
            var owner = PasswordRecoveryOtpService.NewOwner();var id = await otp.Start(Email, owner);
            HttpContext.Session.SetString(PasswordRecoveryOtpService.SessionOwner,owner);
            HttpContext.Session.SetString(PasswordRecoveryOtpService.SessionChallenge,id.ToString());
            return RedirectToPage("/VerifyPasswordOtp");
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            logger.LogWarning("Password recovery database operation failed ({FailureType}).",ex.GetType().Name);
            Error = "Password recovery is temporarily unavailable. Ask the administrator to check the password recovery database setup.";
            return Page();
        }
    }
}
