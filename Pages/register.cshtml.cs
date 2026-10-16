using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;

namespace QMSApplication.Pages
{
    public class RegisterModel : PageModel
    {
        private readonly QMSPortalDbContext _db;
        private readonly PasswordHasher<PortalUser> _passwordHasher;

        public RegisterModel(QMSPortalDbContext db)
        {
            _db = db;
            _passwordHasher = new PasswordHasher<PortalUser>();
        }

        [BindProperty]
        public string? FirstName { get; set; }

        [BindProperty]
        public string? LastName { get; set; }

        [BindProperty]
        public string? Username { get; set; }

        [BindProperty]
        public string? Password { get; set; }

        [BindProperty]
        public string? Email { get; set; }

        [BindProperty]
        public string? MobileNumber { get; set; }

        [BindProperty]
        public string? Otp { get; set; }

        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }
        public bool ShowOtpSection { get; set; }

        private const string DemoOtp = "123456";

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostSendOtpAsync()
        {
            if (string.IsNullOrWhiteSpace(FirstName))
                return SetError("Please enter your first name.");

            if (string.IsNullOrWhiteSpace(LastName))
                return SetError("Please enter your last name.");

            if (string.IsNullOrWhiteSpace(Username))
                return SetError("Please enter your username.");

            if (string.IsNullOrWhiteSpace(Password))
                return SetError("Please enter your password.");

            if (Password.Length < 8)
                return SetError("Password must contain at least 8 characters.");

            if (string.IsNullOrWhiteSpace(Email))
                return SetError("Please enter your email.");

            if (string.IsNullOrWhiteSpace(MobileNumber))
                return SetError("Please enter your mobile number.");

            string username = Username.Trim();
            string email = Email.Trim();
            string mobile = MobileNumber.Trim();

            bool usernameExists = await _db.PortalUsers.AnyAsync(u =>
                u.Username.ToLower() == username.ToLower());

            if (usernameExists)
                return SetError("Username already exists.");

            bool emailExists = await _db.PortalUsers.AnyAsync(u =>
                u.Email != null &&
                u.Email.ToLower() == email.ToLower());

            if (emailExists)
                return SetError("Email is already registered.");

            bool mobileExists = await _db.PortalUsers.AnyAsync(u =>
                u.MobileNumber == mobile);

            if (mobileExists)
                return SetError("Mobile number is already registered.");

            ShowOtpSection = true;
            Message = "Your details have been accepted. Please enter the Demo OTP.";

            return Page();
        }

        public async Task<IActionResult> OnPostRegisterAsync()
        {
            ShowOtpSection = true;

            if (string.IsNullOrWhiteSpace(FirstName) ||
                string.IsNullOrWhiteSpace(LastName) ||
                string.IsNullOrWhiteSpace(Username) ||
                string.IsNullOrWhiteSpace(Password) ||
                string.IsNullOrWhiteSpace(Email) ||
                string.IsNullOrWhiteSpace(MobileNumber))
            {
                ErrorMessage = "Registration details are missing. Please fill in all fields again.";
                return Page();
            }

            if (Password.Length < 8)
            {
                ErrorMessage = "Password must contain at least 8 characters.";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(Otp) || Otp.Trim() != DemoOtp)
            {
                ErrorMessage = "Invalid OTP.";
                return Page();
            }

            string username = Username.Trim();
            string email = Email.Trim();
            string mobile = MobileNumber.Trim();

            bool usernameExists = await _db.PortalUsers.AnyAsync(u =>
                u.Username.ToLower() == username.ToLower());

            bool emailExists = await _db.PortalUsers.AnyAsync(u =>
                u.Email != null &&
                u.Email.ToLower() == email.ToLower());

            bool mobileExists = await _db.PortalUsers.AnyAsync(u =>
                u.MobileNumber == mobile);

            if (usernameExists || emailExists || mobileExists)
            {
                ErrorMessage =
                    "Username, email, or mobile number is already registered. Please verify your details.";
                return Page();
            }

            var user = new PortalUser
            {
                Username = username,
                FullName = FirstName.Trim() + " " + LastName.Trim(),
                Email = email,
                MobileNumber = mobile,
                Role = "User",
                IsActive = true
            };

            user.PasswordHash =
                _passwordHasher.HashPassword(user, Password);

            _db.PortalUsers.Add(user);
            await _db.SaveChangesAsync();

            return RedirectToPage("/Login");
        }

        private IActionResult SetError(string message)
        {
            ErrorMessage = message;
            return Page();
        }
    }
}