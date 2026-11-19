using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QMSApplication.Security;
namespace QMSApplication.Pages;
public class ForgotPasswordModel : PageModel
{
    private readonly PasswordRecoveryOtpService _otp;
    public ForgotPasswordModel(PasswordRecoveryOtpService otp) => _otp = otp;
    [BindProperty] public string? Email { get; set; }
    public string? Error { get; set; }
    public void OnGet() { Response.Headers["Cache-Control"] = "no-store"; }
    public async Task<IActionResult> OnPostAsync()
    {
        OnGet();
        if (!_otp.Ready) { Error = "Email delivery is not configured. Contact the administrator."; return Page(); }
        if (string.IsNullOrWhiteSpace(Email) || Email.Length > 200) { Error = "Enter your registered email address."; return Page(); }
        var owner = PasswordRecoveryOtpService.NewOwner();
        HttpContext.Session.SetString("PasswordRecoveryOwner", owner);
        var id = await _otp.Start(Email, owner);
        HttpContext.Session.SetString("PasswordRecoveryChallenge", id.ToString());
        return RedirectToPage("/VerifyPasswordOtp");
    }
}
