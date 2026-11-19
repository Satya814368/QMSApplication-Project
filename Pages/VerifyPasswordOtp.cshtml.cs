using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Security;
namespace QMSApplication.Pages;
public class VerifyPasswordOtpModel(PasswordRecoveryOtpService otp, ILogger<VerifyPasswordOtpModel> logger) : PageModel
{
    [BindProperty, Required, RegularExpression(@"^[0-9]{6}$")] public string Code { get; set; } = "";
    [BindProperty, Required, StringLength(128, MinimumLength = 12)] public string Password { get; set; } = "";
    [BindProperty, Required, Compare(nameof(Password), ErrorMessage = "Passwords do not match.")] public string ConfirmPassword { get; set; } = "";
    public bool Done { get; set; }
    private void NoCache() { Response.Headers["Cache-Control"] = "no-store";Response.Headers["Referrer-Policy"] = "no-referrer"; }
    private bool HasRequest() => Guid.TryParse(HttpContext.Session.GetString(PasswordRecoveryOtpService.SessionChallenge),out _) && !string.IsNullOrEmpty(HttpContext.Session.GetString(PasswordRecoveryOtpService.SessionOwner));
    public IActionResult OnGet() { NoCache();return HasRequest() ? Page() : RedirectToPage("/forgotpassword"); }
    public async Task<IActionResult> OnPostAsync()
    {
        NoCache();
        if (!HasRequest()) return RedirectToPage("/forgotpassword");
        try
        {
            if (!ModelState.IsValid) return Page();
            var owner = HttpContext.Session.GetString(PasswordRecoveryOtpService.SessionOwner)!;
            var id = Guid.Parse(HttpContext.Session.GetString(PasswordRecoveryOtpService.SessionChallenge)!);
            var error = await otp.Reset(id,owner,Code,Password,ConfirmPassword);
            if (error != null) { ModelState.AddModelError("",error);return Page(); }
            HttpContext.Session.Clear();Done = true;return Page();
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            logger.LogWarning("Password recovery database operation failed ({FailureType}).",ex.GetType().Name);
            ModelState.AddModelError("","Password recovery is temporarily unavailable. Contact the administrator.");return Page();
        }
        finally
        {
            // Never render submitted OTP or password values back into the HTML.
            Password = "";ConfirmPassword = "";Code = "";
            foreach (var key in new[] { nameof(Password), nameof(ConfirmPassword), nameof(Code) })
            {
                var errors = ModelState[key]?.Errors.Select(e => e.ErrorMessage).ToArray() ?? Array.Empty<string>();
                ModelState.Remove(key);
                foreach (var error in errors) ModelState.AddModelError("",error);
            }
        }
    }
}
