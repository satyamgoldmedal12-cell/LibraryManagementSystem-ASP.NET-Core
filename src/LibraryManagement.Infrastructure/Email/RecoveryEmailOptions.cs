using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Infrastructure.Email;

public sealed class RecoveryEmailOptions
{
    [Required, RegularExpression("^(Smtp|Pickup)$")] public string DeliveryMethod { get; set; } = "Smtp";
    [Required, EmailAddress] public string FromAddress { get; set; } = "library@localhost.test";
    public string FromName { get; set; } = "Library Desk";
    public string Host { get; set; } = "";
    [Range(1, 65535)] public int Port { get; set; } = 587;
    public bool SslOnConnect { get; set; }
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
}
