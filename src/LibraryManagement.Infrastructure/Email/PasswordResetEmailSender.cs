using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace LibraryManagement.Infrastructure.Email;

public sealed class PasswordResetEmailSender(IOptions<RecoveryEmailOptions> options, IWebHostEnvironment environment)
    : IPasswordResetEmailSender
{
    public async Task SendAsync(string recipient, string link, CancellationToken ct)
    {
        var settings = options.Value;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = "Reset your Library Desk password";
        message.Body = new TextPart("plain")
        {
            Text = "A password reset was requested for your Library Desk account.\n\n" + link
                + "\n\nThis link expires in 30 minutes and can be used once. If you did not request this, ignore this email."
        };
        if (settings.DeliveryMethod == "Pickup")
        {
            if (!environment.IsDevelopment()) throw new InvalidOperationException("Email pickup is available only in Development.");
            // Not under wwwroot, not served by MVC, and explicitly excluded from Git and publishing.
            var folder = Path.Combine(environment.ContentRootPath, "App_Data", "MailPickup");
            Directory.CreateDirectory(folder);
            await message.WriteToAsync(Path.Combine(folder, Guid.NewGuid().ToString("N") + ".eml"), ct);
            return;
        }
        using var smtp = new MailKit.Net.Smtp.SmtpClient { Timeout = 15000 };
        await smtp.ConnectAsync(settings.Host, settings.Port,
            settings.SslOnConnect ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
        if (!string.IsNullOrWhiteSpace(settings.UserName)) await smtp.AuthenticateAsync(settings.UserName, settings.Password, ct);
        await smtp.SendAsync(message, ct);
        await smtp.DisconnectAsync(true, ct);
    }
}
