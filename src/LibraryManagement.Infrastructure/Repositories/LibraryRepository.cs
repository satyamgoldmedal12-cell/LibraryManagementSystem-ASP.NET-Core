using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Infrastructure.Repositories;

public sealed class LibraryRepository(SqlConnectionFactory factory) : ILibraryRepository
{
    private static CommandDefinition Command(string name, object? args, CancellationToken ct)
    {
        return new CommandDefinition(
            "library." + name,
            args,
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct,
            commandTimeout: 30);
    }

    private async Task<PagedResult<T>> PageAsync<T>(string procedure, object args, string? search, string? status, int page, bool archived, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        using var grid = await connection.QueryMultipleAsync(Command(procedure, args, ct));
        var items = (await grid.ReadAsync<T>()).AsList();
        var total = await grid.ReadSingleAsync<int>();
        return new(items, total, page, 12, search, status, archived);
    }

    private async Task<IReadOnlyList<T>> ListAsync<T>(string procedure, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        return (await connection.QueryAsync<T>(Command(procedure, null, ct))).AsList();
    }

    private async Task<T?> GetAsync<T>(string procedure, int id, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<T>(Command(procedure, new { Id = id }, ct));
    }

    private async Task<int> WriteAsync(string procedure, object args, CancellationToken ct)
    {
        try
        {
            await using var connection = await factory.OpenAsync(ct);
            return await connection.ExecuteScalarAsync<int>(Command(procedure, args, ct));
        }
        catch (SqlException ex) when (ex.Number == 50001)
        {
            throw new BusinessException(ex.Message);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            throw new BusinessException("This ISBN, member number, email, or name already exists.");
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            throw new BusinessException("A referenced record is missing or this record is still in use. Reload and try again.");
        }
        catch (SqlException ex) when (ex.Number == 1205)
        {
            throw new BusinessException("Another operation was running. Please try again.");
        }
    }

    public Task<PagedResult<Book>> BooksAsync(string? search, int page, bool includeArchived, CancellationToken ct) =>
        PageAsync<Book>("Books_List", new { Search = search, Page = page, IncludeArchived = includeArchived }, search, null, page, includeArchived, ct);

    public Task<Book?> BookAsync(int id, CancellationToken ct) => GetAsync<Book>("Books_Get", id, ct);

    public Task<int> SaveBookAsync(Book b, string actor, CancellationToken ct) => WriteAsync("Books_Save", new { b.Id, b.ISBN, b.Title, b.Author, b.Publisher, b.CategoryId, b.TotalCopies, b.Revision, b.IsArchived, ActorId = actor }, ct);

    public Task<BookCover?> BookCoverAsync(int id, CancellationToken ct) => GetAsync<BookCover>("BookCovers_Get", id, ct);

    public Task<int> SaveBookWithCoverAsync(Book b, BookCover? cover, bool removeCover, string actor, CancellationToken ct) =>
        WriteAsync("Books_Save", new
        {
            b.Id,
            b.ISBN,
            b.Title,
            b.Author,
            b.Publisher,
            b.CategoryId,
            b.TotalCopies,
            b.Revision,
            b.IsArchived,
            ActorId = actor,
            CoverImage = cover?.Content,
            CoverContentType = cover?.ContentType,
            RemoveCover = removeCover
        }, ct);

    public async Task DeleteBookAsync(int id, int revision, string actor, CancellationToken ct) =>
        _ = await WriteAsync("Books_Delete", new { Id = id, Revision = revision, ActorId = actor }, ct);

    public Task<IReadOnlyList<Category>> CategoriesAsync(CancellationToken ct) => ListAsync<Category>("Categories_List", ct);

    public async Task SaveCategoryAsync(Category c, string actor, CancellationToken ct) => _ = await WriteAsync("Categories_Save", new { c.Name, ActorId = actor }, ct);

    public Task<PagedResult<Member>> MembersAsync(string? search, int page, CancellationToken ct) =>
        PageAsync<Member>("Members_List", new { Search = search, Page = page }, search, null, page, false, ct);

    public Task<PagedResult<Member>> MembersPageAsync(string? search, int page, bool includeArchived, string status, CancellationToken ct) =>
        PageAsync<Member>("Members_List", new { Search = search, Page = page, IncludeArchived = includeArchived, Status = status }, search, status, page, includeArchived, ct);

    public async Task<MemberStatistics> MemberStatisticsAsync(int id, CancellationToken ct) =>
        (await GetAsync<MemberStatistics>("Members_Statistics", id, ct))!;

    public async Task DeleteMemberAsync(int id, int revision, string actor, CancellationToken ct) =>
        _ = await WriteAsync("Members_Delete", new { Id = id, Revision = revision, ActorId = actor }, ct);

    public async Task RestoreMemberAsync(int id, int revision, string actor, CancellationToken ct) =>
        _ = await WriteAsync("Members_Restore", new { Id = id, Revision = revision, ActorId = actor }, ct);

    public Task<Member?> MemberAsync(int id, CancellationToken ct) => GetAsync<Member>("Members_Get", id, ct);

    public Task<int> SaveMemberAsync(Member m, string actor, CancellationToken ct) => WriteAsync("Members_Save", new { m.Id, m.MemberNumber, m.FullName, m.Email, m.Phone, m.Address, m.JoinedOn, m.ExpiresOn, m.IsActive, m.Revision, ActorId = actor }, ct);

    public Task<PagedResult<Loan>> LoansAsync(string? search, string status, int? memberId, int page, CancellationToken ct) =>
        PageAsync<Loan>("Loans_List", new { Search = search, Status = status, MemberId = memberId, Page = page }, search, status, page, false, ct);

    public async Task<IssueContext> IssueContextAsync(int bookId,int memberId,CancellationToken ct)
    {
        await using var connection=await factory.OpenAsync(ct);
        return await connection.QuerySingleAsync<IssueContext>(Command("Loans_IssueContext",new { BookId=bookId,MemberId=memberId },ct));
    }
    public Task<IssueReceipt?> IssueReceiptAsync(int id,CancellationToken ct) => GetAsync<IssueReceipt>("Loans_Receipt",id,ct);
    public Task<int> IssueAsync(IssueLoan l, LendingOptions p, string actor, CancellationToken ct) => WriteAsync("Loans_Issue", new { l.BookId, l.MemberId, l.DueDate, p.DailyFine, MaxLoans = p.MaxActiveLoans, ActorId = actor }, ct);

    public async Task ReturnAsync(int id, string actor, CancellationToken ct) => _ = await WriteAsync("Loans_Return", new { Id = id, ActorId = actor }, ct);

    public async Task PayFineAsync(int id, string actor, CancellationToken ct) => _ = await WriteAsync("Loans_PayFine", new { Id = id, ActorId = actor }, ct);

    public async Task<Dashboard> DashboardAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        using var grid = await connection.QueryMultipleAsync(Command("Dashboard_Get", null, ct));
        var dashboard = await grid.ReadSingleAsync<Dashboard>();
        dashboard.MonthlyActivity = (await grid.ReadAsync<MonthlyCirculation>()).AsList();
        return dashboard;
    }

    public Task<IReadOnlyList<AuditEvent>> AuditAsync(CancellationToken ct) => ListAsync<AuditEvent>("Audit_List", ct);
}
