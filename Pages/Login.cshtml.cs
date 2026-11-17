using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;

namespace QMSApplication.Pages
{
    public class LoginModel : PageModel
    {
        private readonly QMSPortalDbContext _db;
        private readonly PasswordHasher<PortalUser> _passwordHasher;

        public LoginModel(QMSPortalDbContext db)
        {
            _db = db;
            _passwordHasher = new PasswordHasher<PortalUser>();
        }

        [BindProperty]
        public string? Username { get; set; }

        [BindProperty]
        public string? Password { get; set; }

        public string? Message { get; set; }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                ModelState.AddModelError(
                    nameof(Username),
                    "Please enter your username.");

                return Page();
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                ModelState.AddModelError(
                    nameof(Password),
                    "Please enter your password.");

                return Page();
            }

            var username = Username.Trim();

            var user = await _db.PortalUsers
                .FirstOrDefaultAsync(u =>
                    u.Username == username && u.IsActive && u.IsApproved);

            if (user == null || !QMSApplication.Security.AccessService.KnownRole(user) ||
                string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                Message = "Invalid username or password.";
                return Page();
            }

            PasswordVerificationResult result;
            try { result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, Password); }
            catch (FormatException) { Message = "Invalid username or password. Contact Admin if you need a reset."; return Page(); }

            if (result == PasswordVerificationResult.Failed)
            {
                Message = "Invalid username or password.";
                return Page();
            }

            var firstName = user.FullName.Trim();

            if (string.IsNullOrWhiteSpace(firstName))
            {
                firstName = user.Username;
            }
            else
            {
                // If FullName contains multiple words, use the first one.
                firstName = firstName.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries)[0];
            }

            HttpContext.Session.Clear();
            HttpContext.Session.SetString("SecurityStamp", user.SecurityStamp.ToString());
            HttpContext.Session.SetString("Username", user.Username);
            HttpContext.Session.SetString("LoggedInUser", firstName);
            HttpContext.Session.SetString(
                "UserRole",
                QMSApplication.Data.TeamSetupService.Role(user.Role));

            return RedirectToPage("/Welcome");
        }
    }
}