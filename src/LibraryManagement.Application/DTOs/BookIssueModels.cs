namespace LibraryManagement.Application.DTOs;

public sealed class IssueContext
{
    public int? ConfiguredMaxActiveLoans { get; set; }
    public int? DefaultIssueDays { get; set; }
    public decimal? ConfiguredDailyFine { get; set; }
    public int? BookId { get; set; }
    public string? BookTitle { get; set; }
    public string? ISBN { get; set; }
    public int TotalCopies { get; set; }
    public int AvailableCopies { get; set; }
    public bool BookArchived { get; set; }
    public int? MemberId { get; set; }
    public string? MemberName { get; set; }
    public string? MemberNumber { get; set; }
    public bool MemberActive { get; set; }
    public bool MemberArchived { get; set; }
    public DateTime? MemberExpiresOn { get; set; }
    public int ActiveLoans { get; set; }
    public bool DuplicateActiveIssue { get; set; }
    public bool HasOverdueLoans { get; set; }
}
public sealed record IssueAssessment(IssueContext Context, DateTime IssueDateUtc, DateTime DueDate,
    DateTime MinimumDueDate, DateTime MaximumDueDate, int MaximumBooks, decimal DailyFine, IReadOnlyList<string> Problems)
{
    public bool CanIssue => Context.BookId.HasValue && Context.MemberId.HasValue && Problems.Count==0;
}
public sealed class IssueReceipt
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public int MemberId { get; set; }
    public string BookTitle { get; set; }="";
    public string ISBN { get; set; }="";
    public string MemberName { get; set; }="";
    public string MemberNumber { get; set; }="";
    public DateTime IssuedAt { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public string IssuedByName { get; set; }="";
    public decimal DailyFine { get; set; }
}
