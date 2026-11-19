using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace QMSApplication.Pages;
// Old reset links cannot grant password changes. Always start email OTP recovery.
public class ResetPasswordModel : PageModel
{
    public IActionResult OnGet() { Response.Headers["Cache-Control"]="no-store"; Response.Headers["Referrer-Policy"]="no-referrer";return RedirectToPage("/forgotpassword"); }
    public IActionResult OnPost() => OnGet();
}
