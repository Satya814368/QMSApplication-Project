using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QMSApplication.Security;
namespace QMSApplication.Pages;
public class RegisterModel : PageModel
{
    private readonly RegistrationOtpService _registration;
    public RegisterModel(RegistrationOtpService registration) => _registration = registration;
    [BindProperty, Required, StringLength(100)] public string FirstName { get; set; } = "";
    [BindProperty, Required, StringLength(99)] public string LastName { get; set; } = "";
    [BindProperty, Required, RegularExpression(@"[A-Za-z0-9_.-]{3,100}")] public string Username { get; set; } = "";
    [BindProperty, Required, StringLength(128, MinimumLength = 12)] public string Password { get; set; } = "";
    [BindProperty, Required, EmailAddress, StringLength(200)] public string Email { get; set; } = "";
    [BindProperty, Required, StringLength(20)] public string MobileNumber { get; set; } = "";
    public bool Verified { get; private set; }
    public bool IsDemo => _registration.IsDemo;
    public bool EmailConfigured => _registration.CanSend;
    public void OnGet()
    {
        Response.Headers["Cache-Control"] = "no-store";
        Verified = TempData["RegistrationVerified"] is true;
    }
    public async Task<IActionResult> OnPostAsync()
    {
        Response.Headers["Cache-Control"] = "no-store";
        if (!ModelState.IsValid) return Page();
        var owner = HttpContext.Session.GetString(RegistrationOtpService.SessionOwner) ?? RegistrationOtpService.NewOwner();
        // Persist before sending: this ties the challenge and later code to this browser session.
        HttpContext.Session.SetString(RegistrationOtpService.SessionOwner, owner);
        var result = await _registration.Start(Username, FirstName + " " + LastName, Email, MobileNumber, Password, owner);
        Password = ""; ModelState.Remove(nameof(Password));
        if (result.Error != null) { ModelState.AddModelError("", result.Error); return Page(); }
        HttpContext.Session.SetString(RegistrationOtpService.SessionChallenge, result.Id!.Value.ToString());
        return RedirectToPage("/VerifyRegistration");
    }
}
