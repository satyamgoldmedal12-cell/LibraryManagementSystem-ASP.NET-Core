namespace LibraryManagement.Application.Services;

public sealed class LibraryQueries(ILibraryRepository repository) : ILibraryQueries
{
    public Task<PagedResult<Book>> BooksAsync(string? search, int page, bool includeArchived, CancellationToken ct) => repository.BooksAsync(search, page, includeArchived, ct);

    public Task<BookCover?> BookCoverAsync(int id, CancellationToken ct) => repository.BookCoverAsync(id, ct);

    public Task<Book?> BookAsync(int id, CancellationToken ct) => repository.BookAsync(id, ct);

    public Task<IReadOnlyList<Category>> CategoriesAsync(CancellationToken ct) => repository.CategoriesAsync(ct);

    public Task<PagedResult<Member>> MembersAsync(string? search, int page, CancellationToken ct) => repository.MembersAsync(search, page, ct);

    public Task<Member?> MemberAsync(int id, CancellationToken ct) => repository.MemberAsync(id, ct);

    public Task<PagedResult<Loan>> LoansAsync(string? search, string status, int? memberId, int page, CancellationToken ct) => repository.LoansAsync(search, status, memberId, page, ct);

    public Task<Dashboard> DashboardAsync(CancellationToken ct) => repository.DashboardAsync(ct);

    public Task<IReadOnlyList<AuditEvent>> AuditAsync(CancellationToken ct) => repository.AuditAsync(ct);
}
