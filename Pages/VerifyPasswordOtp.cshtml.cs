using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QMSApplication.Security;
namespace QMSApplication.Pages;
public class VerifyPasswordOtpModel : PageModel
{
    private readonly PasswordRecoveryOtpService _otp;
    public VerifyPasswordOtpModel(PasswordRecoveryOtpService otp) => _otp = otp;
    [BindProperty, Required, RegularExpression(@"^[0-9]{6}$")] public string Code { get; set; } = "";
    [BindProperty, Required, StringLength(128, MinimumLength = 12)] public string Password { get; set; } = "";
    [BindProperty, Required] public string ConfirmPassword { get; set; } = "";
    public bool Done { get; set; }
    public void OnGet() { Response.Headers["Cache-Control"] = "no-store"; Response.Headers["Referrer-Policy"] = "no-referrer"; }
    public async Task<IActionResult> OnPostAsync()
    {
        OnGet();
        if (!ModelState.IsValid) return Page();
        var owner = HttpContext.Session.GetString("PasswordRecoveryOwner") ?? "";
        if (!Guid.TryParse(HttpContext.Session.GetString("PasswordRecoveryChallenge"), out var id))
        { ModelState.AddModelError("", "Start password recovery again."); return Page(); }
        var error = await _otp.Reset(id, owner, Code, Password, ConfirmPassword);
        Password = ""; ConfirmPassword = ""; Code = "";
        if (error != null) { ModelState.AddModelError("", error); return Page(); }
        HttpContext.Session.Clear(); Done = true;
        return Page();
    }
}
