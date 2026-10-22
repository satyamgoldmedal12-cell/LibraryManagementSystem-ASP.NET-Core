using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Application.DTOs;

public sealed class ForgotPasswordModel
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
}
public sealed class PublicResetPasswordModel
{
    [Required, StringLength(450)] public string UserId { get; set; } = "";
    [Required, StringLength(4096)] public string Code { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password), Display(Name = "New password")]
    public string NewPassword { get; set; } = "";
    [Required, StringLength(128), Compare(nameof(NewPassword)), DataType(DataType.Password), Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = "";
}
public sealed class AuthenticationSessionOptions
{
    [Range(1, 120)] public int IdleMinutes { get; set; } = 30;
    [Range(1, 24)] public int AbsoluteHours { get; set; } = 8;
}
public sealed class PasswordRecoveryOptions
{
    [Required] public string PublicOrigin { get; set; } = "";
}
public sealed class UserSession
{
    public Guid Id { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Device { get; set; } = "";
}
public sealed record SessionListModel(IReadOnlyList<UserSession> Sessions, Guid CurrentSessionId);
