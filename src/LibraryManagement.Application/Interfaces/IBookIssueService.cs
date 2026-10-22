namespace LibraryManagement.Application.Interfaces;

public interface IBookIssueService
{
    Task<IssueAssessment> AssessAsync(int bookId,int memberId,DateTime? dueDate,CancellationToken ct);
    Task<int> IssueAsync(IssueLoan loan,string actor,CancellationToken ct);
    Task<IssueReceipt?> ReceiptAsync(int id,CancellationToken ct);
}
