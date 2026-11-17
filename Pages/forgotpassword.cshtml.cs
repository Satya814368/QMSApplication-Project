using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QMSApplication.Security;

namespace QMSApplication.Pages;
public class ForgotPasswordModel : PageModel
{
    private readonly AccountService _accounts;
    public ForgotPasswordModel(AccountService accounts) => _accounts = accounts;
    [BindProperty]
    public string? UsernameOrEmail { get; set; }

    public string? Message { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!string.IsNullOrWhiteSpace(UsernameOrEmail))
            await _accounts.RequestReset(UsernameOrEmail);
        Message = "If an eligible account matches, the request is available to Admin. Contact Admin to verify your identity and obtain a single-use reset link.";
        return Page();
    }
}
