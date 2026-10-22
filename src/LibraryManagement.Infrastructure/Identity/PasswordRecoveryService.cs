using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace LibraryManagement.Infrastructure.Identity;

public sealed class PasswordRecoveryService(UserManager<ApplicationUser> users, IPasswordResetEmailSender sender,
    IUserSessionRepository sessions, IOptions<PasswordRecoveryOptions> options, ILogger<PasswordRecoveryService> logger)
    : IPasswordRecoveryService
{
    private static OperationResult InvalidLink() => OperationResult.Failed("InvalidToken", "This reset link is invalid or expired. Request a new link.");

    public async Task SendResetAsync(string email, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null || user.LockoutEnd == DateTimeOffset.MaxValue || !await users.HasPasswordAsync(user)) return;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        // Use a trusted configured origin, never an incoming Host header.
        var link = QueryHelpers.AddQueryString(options.Value.PublicOrigin.TrimEnd('/') + "/Account/ResetPassword",
            new Dictionary<string, string?> { ["userId"] = user.Id, ["code"] = code });
        try { await sender.SendAsync(user.Email!, link, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // SMTP exception messages can contain recipient details; never log a reset link or token.
            logger.LogError("Password recovery delivery failed ({ErrorType}). Check the email configuration.", ex.GetType().Name);
        }
    }

    public async Task<OperationResult> ResetAsync(PublicResetPasswordModel model, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(model.UserId);
        if (user is null || user.LockoutEnd == DateTimeOffset.MaxValue) return InvalidLink();
        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(model.Code)); }
        catch (FormatException) { return InvalidLink(); }
        // These fields are persisted only if Identity validates the token and new password successfully.
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        user.AuditActorId=user.Id;user.AuditAction="Password reset";
        var result = await users.ResetPasswordAsync(user, token, model.NewPassword);
        if (!result.Succeeded)
            return result.Errors.Any(e => e.Code == "InvalidToken") ? InvalidLink() : result.ToOperationResult();
        await sessions.RevokeAllAsync(user.Id, null, ct);
        logger.LogInformation("Password recovered for user {UserId}", user.Id);
        return OperationResult.Success;
    }
}
