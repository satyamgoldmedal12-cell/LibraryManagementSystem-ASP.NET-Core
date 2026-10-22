using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;
using Xunit;

namespace LibraryManagement.Tests;

public sealed class RecoveryEmailTests
{
    [Fact]
    public async Task Development_pickup_creates_private_mime_email_outside_webroot()
    {
        var root = Path.Combine(Path.GetTempPath(), "LibraryMailTest-" + Guid.NewGuid().ToString("N"));
        var environment = Mock.Of<IWebHostEnvironment>(e => e.EnvironmentName == "Development" && e.ContentRootPath == root);
        var sender = new PasswordResetEmailSender(Options.Create(new RecoveryEmailOptions { DeliveryMethod = "Pickup" }), environment);
        try
        {
            await sender.SendAsync("reader@example.test", "https://library.example.test/Account/ResetPassword?code=TEST", default);
            var file = Assert.Single(Directory.GetFiles(Path.Combine(root, "App_Data", "MailPickup"), "*.eml"));
            var message = await MimeMessage.LoadAsync(file);
            Assert.Equal("reader@example.test", Assert.Single(message.To.Mailboxes).Address);
            Assert.Contains("https://library.example.test/Account/ResetPassword?code=TEST", message.TextBody);
            Assert.False(Directory.Exists(Path.Combine(root, "wwwroot")));
        }
        finally
        {
            // Only remove this test's unique generated directory, verified under the OS temporary root.
            var full = Path.GetFullPath(root);
            if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(full).StartsWith("LibraryMailTest-", StringComparison.Ordinal) && Directory.Exists(full))
                Directory.Delete(full, true);
        }
    }
    [Fact]
    public async Task Production_never_writes_reset_links_to_pickup_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "LibraryMailTest-" + Guid.NewGuid().ToString("N"));
        var environment = Mock.Of<IWebHostEnvironment>(e => e.EnvironmentName == "Production" && e.ContentRootPath == root);
        var sender = new PasswordResetEmailSender(Options.Create(new RecoveryEmailOptions { DeliveryMethod = "Pickup" }), environment);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync("reader@example.test", "https://library.example.test/Account/ResetPassword?code=TEST", default));
        Assert.False(Directory.Exists(root));
    }
}
