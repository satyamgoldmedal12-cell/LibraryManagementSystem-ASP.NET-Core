using Microsoft.AspNetCore.Identity;
using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Enums;
using LibraryManagement.Domain.Exceptions;
using LibraryManagement.Domain.Rules;

namespace LibraryManagement.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = "";
    public string Role { get; set; } = Roles.Librarian;
    public int? MemberId { get; set; }
    public bool IsActive { get; set; } = true;
    public int Revision { get; set; } = 1;
    internal string? AuditActorId { get; set; }
    internal string? AuditAction { get; set; }
    internal int? ExpectedRevision { get; set; }
    // Transient provisioning instruction; never bound from a public registration form or stored as a column.
    internal bool CreateMembershipOnRegistration { get; set; }
}
