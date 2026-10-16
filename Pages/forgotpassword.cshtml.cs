using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;

namespace QMSApplication.Pages
{
    public class ForgotPasswordModel : PageModel
    {
        private readonly QMSPortalDbContext _db;
        private readonly PasswordHasher<PortalUser> _passwordHasher;

        public ForgotPasswordModel(QMSPortalDbContext db)
        {
            _db = db;
            _passwordHasher = new PasswordHasher<PortalUser>();
        }

        [BindProperty]
        public string? UsernameOrEmail { get; set; }

        [BindProperty]
        public string? MobileNumber { get; set; }

        [BindProperty]
        public string? Otp { get; set; }

        [BindProperty]
        public string? NewPassword { get; set; }

        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }
        public bool ShowResetSection { get; set; }

        private const string DemoOtp = "123456";

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostVerifyAsync()
        {
            if (string.IsNullOrWhiteSpace(UsernameOrEmail))
            {
                ErrorMessage = "Please enter your username or email.";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(MobileNumber))
            {
                ErrorMessage = "Please enter your mobile number.";
                return Page();
            }

            string identifier = UsernameOrEmail.Trim();
            string mobile = MobileNumber.Trim();

            var user = await _db.PortalUsers.FirstOrDefaultAsync(u =>
                u.IsActive &&
                (u.Username == identifier || u.Email == identifier) &&
                u.MobileNumber == mobile);

            if (user == null)
            {
                ErrorMessage =
                    "Username/Email and Mobile Number do not match.";
                return Page();
            }

            ShowResetSection = true;
            Message =
                "Verification successful. Please enter the Demo OTP and your new password.";

            return Page();
        }

        public async Task<IActionResult> OnPostResetAsync()
        {
            ShowResetSection = true;

            if (string.IsNullOrWhiteSpace(UsernameOrEmail) ||
                string.IsNullOrWhiteSpace(MobileNumber))
            {
                ErrorMessage =
                    "Please verify your username/email and mobile number first.";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(Otp) ||
                Otp.Trim() != DemoOtp)
            {
                ErrorMessage = "Invalid OTP.";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(NewPassword))
            {
                ErrorMessage = "Please enter a new password.";
                return Page();
            }

            if (NewPassword.Length < 8)
            {
                ErrorMessage =
                    "The new password must contain at least 8 characters.";
                return Page();
            }

            string identifier = UsernameOrEmail.Trim();
            string mobile = MobileNumber.Trim();

            var user = await _db.PortalUsers.FirstOrDefaultAsync(u =>
                u.IsActive &&
                (u.Username == identifier || u.Email == identifier) &&
                u.MobileNumber == mobile);

            if (user == null)
            {
                ErrorMessage =
                    "User verification failed. Please start again.";
                ShowResetSection = false;
                return Page();
            }

            user.PasswordHash =
                _passwordHasher.HashPassword(user, NewPassword);

            await _db.SaveChangesAsync();

            return RedirectToPage("/Login");
        }
    }
}