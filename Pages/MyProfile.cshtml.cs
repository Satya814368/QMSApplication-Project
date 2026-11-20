using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QMSApplication.Models;
using QMSApplication.Security;
namespace QMSApplication.Pages;
public class MyProfileModel(QMSPortalDbContext db, AccessService access) : PageModel
{
    public ProfileService.Details Profile { get; private set; } = null!;
    public IActionResult OnPost() => StatusCode(StatusCodes.Status405MethodNotAllowed);
    public async Task<IActionResult> OnGetAsync()
    {
        Response.Headers["Cache-Control"] = "no-store";
        var actor = await access.Current(HttpContext);
        if (actor == null) return RedirectToPage("/Login");
        Profile = await new ProfileService(db).Load(actor);
        return Page();
    }
}
