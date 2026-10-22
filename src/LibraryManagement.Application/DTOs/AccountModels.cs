using System.ComponentModel.DataAnnotations;
using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Enums;
using LibraryManagement.Domain.Exceptions;
using LibraryManagement.Domain.Rules;

namespace LibraryManagement.Application.DTOs;

public sealed class RegisterModel
{
    [Required, StringLength(120), Display(Name = "Full name")] public string FullName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Required, StringLength(128), Compare(nameof(Password)), DataType(DataType.Password), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";
}

public sealed class LoginModel
{
    [RegularExpression("^(Admin|Librarian|Member)$")] public string? Portal { get; set; }
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password), StringLength(128)] public string Password { get; set; } = "";
    [Display(Name = "Remember me")] public bool RememberMe { get; set; }
    [StringLength(2048)] public string? ReturnUrl { get; set; }
}

public sealed class CreateUserModel
{
    [Required, StringLength(120), Display(Name = "Full name")] public string FullName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Required, RegularExpression("^(Admin|Librarian|Member)$")] public string Role { get; set; } = Roles.Librarian;
    [Range(1, int.MaxValue), Display(Name = "Member ID (for Member accounts)")] public int? MemberId { get; set; }
}

public sealed class ChangePasswordModel
{
    [Required, StringLength(128), DataType(DataType.Password), Display(Name = "Current password")] public string CurrentPassword { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password), Display(Name = "New password")] public string NewPassword { get; set; } = "";
    [Required, StringLength(128), Compare(nameof(NewPassword)), DataType(DataType.Password), Display(Name = "Confirm new password")] public string ConfirmPassword { get; set; } = "";
}

public sealed class ResetPasswordModel
{
    [Required, StringLength(450)] public string Id { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)] public string NewPassword { get; set; } = "";
}
