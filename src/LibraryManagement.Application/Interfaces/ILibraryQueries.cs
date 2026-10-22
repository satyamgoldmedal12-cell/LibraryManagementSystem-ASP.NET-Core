using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Enums;
using LibraryManagement.Domain.Exceptions;
using LibraryManagement.Domain.Rules;
using LibraryManagement.Application.DTOs;

namespace LibraryManagement.Application.Interfaces;

public interface ILibraryQueries
{
    Task<PagedResult<Book>> BooksAsync(string? search, int page, bool includeArchived, CancellationToken ct);
    Task<Book?> BookAsync(int id, CancellationToken ct);
    Task<BookCover?> BookCoverAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<Category>> CategoriesAsync(CancellationToken ct);
    Task<PagedResult<Member>> MembersAsync(string? search, int page, CancellationToken ct);
    Task<Member?> MemberAsync(int id, CancellationToken ct);
    Task<PagedResult<Loan>> LoansAsync(string? search, string status, int? memberId, int page, CancellationToken ct);
    Task<Dashboard> DashboardAsync(CancellationToken ct);
    Task<IReadOnlyList<AuditEvent>> AuditAsync(CancellationToken ct);
}
