using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Security;
namespace QMSApplication.Pages;
public class ChangePasswordModel(AccessService access, PasswordRecoveryOtpService otp, ILogger<ChangePasswordModel> logger) : PageModel
{
    private const string OwnerKey = "ProfilePasswordOwner", ChallengeKey = "ProfilePasswordChallenge", UserKey = "ProfilePasswordUser";
    [BindProperty] public string? Code { get; set; }
    [BindProperty] public string? Password { get; set; }
    [BindProperty] public string? ConfirmPassword { get; set; }
    public string MaskedEmail { get; private set; } = "";
    public bool CanSend { get; private set; }
    public bool HasRequest { get; private set; }
    public bool Done { get; private set; }
    private void NoCache() { Response.Headers["Cache-Control"] = "no-store"; Response.Headers["Referrer-Policy"] = "no-referrer"; }
    private async Task<QMSApplication.Models.PortalUser?> Load()
    {
        NoCache();var actor = await access.Current(HttpContext);if (actor == null) return null;
        var email = actor.Email?.Trim() ?? "";var at = email.LastIndexOf('@');
        var valid = new EmailAddressAttribute().IsValid(email) && at > 0;
        MaskedEmail = valid ? email[..1] + "***" + email[at..] : "No valid registered email";
        CanSend = otp.Ready && valid;
        HasRequest = HttpContext.Session.GetString(UserKey) == actor.Id.ToString() && Guid.TryParse(HttpContext.Session.GetString(ChallengeKey), out _) && !string.IsNullOrEmpty(HttpContext.Session.GetString(OwnerKey));
        return actor;
    }
    public async Task<IActionResult> OnGetAsync() => await Load() == null ? RedirectToPage("/Login") : Page();
    public async Task<IActionResult> OnPostSendAsync()
    {
        var actor = await Load();if (actor == null) return RedirectToPage("/Login");
        if (!CanSend) { ModelState.AddModelError("", "A valid registered email and configured email delivery are required. Contact Admin.");return Page(); }
        try
        {
            var owner = PasswordRecoveryOtpService.NewOwner();var id = await otp.Start(actor.Email!, owner);
            HttpContext.Session.SetString(OwnerKey,owner);HttpContext.Session.SetString(ChallengeKey,id.ToString());HttpContext.Session.SetString(UserKey,actor.Id.ToString());
            TempData["ProfileOtpSent"] = "If your account is eligible and the request limit allows, an OTP has been sent to your registered email. Check your inbox and spam folder.";
            return RedirectToPage();
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        { LogFailure(ex);return Page(); }
    }
    public async Task<IActionResult> OnPostVerifyAsync()
    {
        var actor = await Load();if (actor == null) return RedirectToPage("/Login");
        try
        {
            if (!HasRequest) { ModelState.AddModelError("", "Request an OTP in this browser first.");return Page(); }
            if (Code?.Length != 6 || !Code.All(c => c is >= '0' and <= '9')) ModelState.AddModelError("", "Enter the six-digit OTP from your email.");
            if (Password == null || Password.Length is < 12 or > 128) ModelState.AddModelError("", "Use a new password between 12 and 128 characters.");
            if (Password != ConfirmPassword) ModelState.AddModelError("", "Passwords do not match.");
            if (!ModelState.IsValid) return Page();
            var id = Guid.Parse(HttpContext.Session.GetString(ChallengeKey)!);var owner = HttpContext.Session.GetString(OwnerKey)!;
            var error = await otp.Reset(id,owner,Code!,Password!,ConfirmPassword!);
            if (error != null) { ModelState.AddModelError("",error);return Page(); }
            HttpContext.Session.Clear();HasRequest = false;Done = true;return Page();
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        { LogFailure(ex);return Page(); }
        finally
        {
            Code = Password = ConfirmPassword = null;
            ModelState.Remove(nameof(Code));ModelState.Remove(nameof(Password));ModelState.Remove(nameof(ConfirmPassword));
        }
    }
    private void LogFailure(Exception ex)
    {
        logger.LogWarning("Profile password operation failed ({FailureType}).",ex.GetType().Name);
        ModelState.AddModelError("", "Password change is temporarily unavailable. Ask Admin to check the password recovery database setup.");
    }
}
