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
                    u.Username == username && u.IsActive);

            if (user == null ||
                string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                Message = "Invalid username or password.";
                return Page();
            }

            var result = _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                Password);

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

            HttpContext.Session.SetString("Username", user.Username);
            HttpContext.Session.SetString("LoggedInUser", firstName);
            HttpContext.Session.SetString(
                "UserRole",
                string.IsNullOrWhiteSpace(user.Role) ? "User" : user.Role);

            return RedirectToPage("/Welcome");
        }
    }
}