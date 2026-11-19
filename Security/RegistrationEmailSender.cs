using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
namespace QMSApplication.Security;

public class RegistrationEmailOptions
{
    public bool Enabled { get; set; }
    public bool DemoMode { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Project Management Tool";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
public interface IRegistrationEmailSender
{
    bool IsConfigured { get; }
    bool IsDemo => false;
    string? DemoCode => null;
    Task SendAsync(string recipient, string code, CancellationToken cancellationToken = default);
    Task SendPasswordResetAsync(string recipient, string code, CancellationToken cancellationToken = default);
}
public sealed class RegistrationEmailDeliveryException : Exception { }

public sealed class SmtpRegistrationEmailSender : IRegistrationEmailSender
{
    private readonly IOptionsMonitor<RegistrationEmailOptions> _options;
    public const string DemoCodeSessionKey = "RegistrationDemoCode";
    private readonly ILogger<SmtpRegistrationEmailSender> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly IHttpContextAccessor _http;
    public bool IsDemo => _options.CurrentValue.DemoMode && _environment.IsDevelopment();
    public string? DemoCode => IsDemo ? _http.HttpContext?.Session.GetString(DemoCodeSessionKey) : null;
    public SmtpRegistrationEmailSender(IOptionsMonitor<RegistrationEmailOptions> options, ILogger<SmtpRegistrationEmailSender> logger, IWebHostEnvironment environment, IHttpContextAccessor http)
    { _options = options; _logger = logger; _environment = environment; _http = http; }
    public bool IsConfigured
    {
        get
        {
            var o = _options.CurrentValue;
            if (o.DemoMode) return IsDemo;
            return o.Enabled && !string.IsNullOrWhiteSpace(o.Host) && o.Port is > 0 and <= 65535 && o.Port != 465 &&
                MailAddress.TryCreate(o.FromAddress, out _) &&
                (string.IsNullOrEmpty(o.Username) == string.IsNullOrEmpty(o.Password));
        }
    }
    public Task SendAsync(string recipient, string code, CancellationToken cancellationToken = default)
        => SendCoreAsync(recipient, code, false, cancellationToken);
    public Task SendPasswordResetAsync(string recipient, string code, CancellationToken cancellationToken = default)
        => SendCoreAsync(recipient, code, true, cancellationToken);
    private async Task SendCoreAsync(string recipient, string code, bool recovery, CancellationToken cancellationToken)
    {
        if (recovery && IsDemo) throw new RegistrationEmailDeliveryException();
        if (!IsConfigured) throw new RegistrationEmailDeliveryException();
        if (IsDemo)
        {
            // Demo only: show the fixed code to this registration browser, never send email.
            _http.HttpContext?.Session.SetString(DemoCodeSessionKey, DemoRegistrationService.Code);
            return;
        }
        var o = _options.CurrentValue;
        try
        {
            using var message = new MailMessage();
            message.From = new MailAddress(o.FromAddress, o.FromName);
            message.To.Add(new MailAddress(recipient));
            message.Subject = recovery ? "Your password reset verification code" : "Your registration verification code";
            message.Body = recovery ? $"Your Project Management Tool password reset code is: {code}\n\nIt expires in 5 minutes. Do not share it. If you did not request a reset, ignore this email." : $"Your Project Management Tool registration code is: {code}\n\nIt expires in 5 minutes. Do not share it. If you did not request this registration, ignore this email.\n\nEmail verification is followed by Admin approval before you can sign in.";
            using var client = new SmtpClient(o.Host, o.Port) { EnableSsl = true, UseDefaultCredentials = false, DeliveryMethod = SmtpDeliveryMethod.Network };
            if (!string.IsNullOrEmpty(o.Username)) client.Credentials = new NetworkCredential(o.Username, o.Password);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await client.SendMailAsync(message, timeout.Token);
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or IOException or OperationCanceledException or FormatException)
        {
            // Do not log the OTP, SMTP password or pending registration details.
            _logger.LogWarning("Registration email delivery failed ({FailureType}).", ex.GetType().Name);
            throw new RegistrationEmailDeliveryException();
        }
    }
}
