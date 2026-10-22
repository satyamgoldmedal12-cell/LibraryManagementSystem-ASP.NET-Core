using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Domain.Entities;

public sealed class Loan : AuditedEntity
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public int MemberId { get; set; }
    public string Title { get; set; } = "";
    public string MemberName { get; set; } = "";
    public string MemberNumber { get; set; } = "";
    public DateTime IssuedAt { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public decimal FineAmount { get; set; }
    public bool IsPaid { get; set; }
    public bool IsOverdue => ReturnedAt is null && DueDate.Date < LibraryManagement.Domain.Rules.IndiaTime.Today;
}
