using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Enums;
using LibraryManagement.Domain.Exceptions;
using LibraryManagement.Domain.Rules;
using LibraryManagement.Application.DTOs;

namespace LibraryManagement.Application.Interfaces;

public interface ILibraryRepository
{
    Task<PagedResult<Book>> BooksAsync(string? search, int page, bool includeArchived, CancellationToken ct);
    Task<Book?> BookAsync(int id, CancellationToken ct);
    Task<BookCover?> BookCoverAsync(int id, CancellationToken ct);
    Task<int> SaveBookAsync(Book book, string actor, CancellationToken ct);
    Task<int> SaveBookWithCoverAsync(Book book, BookCover? cover, bool removeCover, string actor, CancellationToken ct);
    Task DeleteBookAsync(int id, int revision, string actor, CancellationToken ct);
    Task<IReadOnlyList<Category>> CategoriesAsync(CancellationToken ct);
    Task SaveCategoryAsync(Category category, string actor, CancellationToken ct);
    Task<PagedResult<Member>> MembersAsync(string? search, int page, CancellationToken ct);
    Task<PagedResult<Member>> MembersPageAsync(string? search, int page, bool includeArchived, string status, CancellationToken ct);
    Task<MemberStatistics> MemberStatisticsAsync(int id, CancellationToken ct);
    Task DeleteMemberAsync(int id, int revision, string actor, CancellationToken ct);
    Task RestoreMemberAsync(int id, int revision, string actor, CancellationToken ct);
    Task<Member?> MemberAsync(int id, CancellationToken ct);
    Task<int> SaveMemberAsync(Member member, string actor, CancellationToken ct);
    Task<PagedResult<Loan>> LoansAsync(string? search, string status, int? memberId, int page, CancellationToken ct);
    Task<IssueContext> IssueContextAsync(int bookId, int memberId, CancellationToken ct);
    Task<IssueReceipt?> IssueReceiptAsync(int id, CancellationToken ct);
    Task<int> IssueAsync(IssueLoan loan, LendingOptions policy, string actor, CancellationToken ct);
    Task ReturnAsync(int id, string actor, CancellationToken ct);
    Task PayFineAsync(int id, string actor, CancellationToken ct);
    Task<Dashboard> DashboardAsync(CancellationToken ct);
    Task<IReadOnlyList<AuditEvent>> AuditAsync(CancellationToken ct);
}
