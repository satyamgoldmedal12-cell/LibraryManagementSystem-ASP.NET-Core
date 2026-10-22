namespace LibraryManagement.Application.Interfaces;

public interface ILibraryService
{
    Task<int> SaveBookAsync(Book book, string actor, CancellationToken ct);
    Task<int> SaveMemberAsync(Member member, string actor, CancellationToken ct);
    Task SaveCategoryAsync(Category category, string actor, CancellationToken ct);
    Task<int> IssueAsync(IssueLoan loan, string actor, CancellationToken ct);
    Task ReturnAsync(int id, string actor, CancellationToken ct);
    Task PayFineAsync(int id, string actor, CancellationToken ct);
}

