using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Domain.Entities;

public sealed class Member : AuditedEntity
{
    public int Id { get; set; }
    [Required, StringLength(30), RegularExpression(@"[A-Za-z0-9-]+")]
    public string MemberNumber { get; set; } = "";
    [Required, StringLength(120)] public string FullName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [StringLength(25), Phone] public string? Phone { get; set; }
    [StringLength(500)] public string? Address { get; set; }
    public DateTime? JoinedOn { get; set; }
    public bool IsArchived { get; set; }
    public string Status => IsArchived ? "Deleted" : !IsActive ? "Inactive" : ExpiresOn.Date < LibraryManagement.Domain.Rules.IndiaTime.Today ? "Expired" : "Active";
    [Required]
    public DateTime ExpiresOn { get; set; } = LibraryManagement.Domain.Rules.IndiaTime.Today.AddYears(1);
    public bool IsActive { get; set; } = true;
    public int Revision { get; set; }
}
