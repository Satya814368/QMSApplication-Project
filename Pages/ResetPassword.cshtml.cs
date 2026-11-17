using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QMSApplication.Security;

namespace QMSApplication.Pages;
public class ResetPasswordModel : PageModel
{
    private readonly AccountService _accounts;
    public ResetPasswordModel(AccountService accounts) => _accounts = accounts;
    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty]
    public string? ConfirmPassword { get; set; }

    public bool Done { get; set; }

    public void OnGet()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["Cache-Control"] = "no-store";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        OnGet();
        if (Password != ConfirmPassword)
        {
            ModelState.AddModelError("", "Passwords do not match.");
            return Page();
        }

        var error = await _accounts.Reset(Token ?? "", Password ?? "");
        if (error != null)
            ModelState.AddModelError("", error);
        else
        {
            HttpContext.Session.Clear();
            Done = true;
        }

        return Page();
    }
}
