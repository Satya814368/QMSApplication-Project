using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QMSApplication.Security;
namespace QMSApplication.Pages;
public class VerifyRegistrationModel : PageModel
{
    private readonly RegistrationOtpService _registration;
    public VerifyRegistrationModel(RegistrationOtpService registration) => _registration = registration;
    [BindProperty, Required, RegularExpression(@"[0-9]{6}", ErrorMessage = "Enter the six-digit email code.")]
    public string? Code { get; set; }
    public RegistrationOtpService.Status? Pending { get; private set; }
    public string? Message { get; private set; }
    public bool IsDemo => _registration.IsDemo;
    public string? DemoCode => Pending != null ? _registration.DemoCode : null;
    private bool Session(out Guid id, out string owner)
    {
        owner = HttpContext.Session.GetString(RegistrationOtpService.SessionOwner) ?? "";
        return Guid.TryParse(HttpContext.Session.GetString(RegistrationOtpService.SessionChallenge), out id) && owner.Length >= 40;
    }
    private void Headers()
    { Response.Headers["Cache-Control"] = "no-store"; Response.Headers["Referrer-Policy"] = "no-referrer"; }
    private async Task Load()
    { Headers(); if (Session(out var id, out var owner)) Pending = await _registration.Get(id, owner); }
    public async Task<IActionResult> OnGetAsync()
    {
        await Load();
        return Page();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        Headers();
        if (!Session(out var id, out var owner)) { ModelState.AddModelError("", "Start registration in this browser before entering a code."); return Page(); }
        // Count malformed as well as incorrect codes towards the same five-attempt limit.
        var error = await _registration.Verify(id, owner, Code ?? "");
        Code = ""; ModelState.Remove(nameof(Code));
        if (error != null) { ModelState.AddModelError("", error); await Load(); return Page(); }
        HttpContext.Session.Remove(SmtpRegistrationEmailSender.DemoCodeSessionKey);
        HttpContext.Session.Remove(RegistrationOtpService.SessionChallenge);
        HttpContext.Session.Remove(RegistrationOtpService.SessionOwner);
        TempData["RegistrationVerified"] = true;
        return RedirectToPage("/register");
    }
    public async Task<IActionResult> OnPostResendAsync()
    {
        ModelState.Remove(nameof(Code)); Headers();
        if (!Session(out var id, out var owner)) { ModelState.AddModelError("", "Start registration again in this browser."); return Page(); }
        var error = await _registration.Resend(id, owner);
        if (error != null) ModelState.AddModelError("", error); else Message = IsDemo ? "Demo code 123456 is ready again. Enter it below." : "A new code was sent. Use the latest email; the previous code no longer works.";
        await Load(); return Page();
    }
    public async Task<IActionResult> OnPostStartOverAsync()
    {
        if (Session(out var id, out var owner)) await _registration.Cancel(id, owner);
        HttpContext.Session.Remove(SmtpRegistrationEmailSender.DemoCodeSessionKey);
        HttpContext.Session.Remove(RegistrationOtpService.SessionChallenge);
        // Keep the owner secret so restarting cannot bypass the 60-second session cooldown.
        return RedirectToPage("/register");
    }
}
