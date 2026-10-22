using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LibraryManagement.Tests;

public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LIBRARY_TEST_CONNECTION")))
            Skip = "Set LIBRARY_TEST_CONNECTION to a database whose name ends with _Test and apply Database scripts first.";
    }
}

[Collection("MVC host")]
public sealed class SqlIntegrationTests
{
    private static SqlConnectionFactory Factory()
    {
        var value = Environment.GetEnvironmentVariable("LIBRARY_TEST_CONNECTION")!;
        if (!new SqlConnectionStringBuilder(value).InitialCatalog.EndsWith("_Test", StringComparison.Ordinal))
            throw new InvalidOperationException("Integration tests require a dedicated database ending in _Test.");
        return new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:LibraryDatabase"] = value }).Build());
    }
    private static async Task<(LibraryRepository Repo, string Actor, int Book, int Member)> SeedAsync(int copies = 1)
    {
        var factory = Factory();
        var store = new DapperUserStore(factory, new IdentityErrorDescriber());
        var suffix = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { UserName = suffix, NormalizedUserName = suffix.ToUpperInvariant(), Email = suffix + "@example.test", NormalizedEmail = suffix.ToUpperInvariant() + "@EXAMPLE.TEST", FullName = "Integration Test", Role = Roles.Librarian };
        Assert.True((await store.CreateAsync(user, default)).Succeeded);
        var repo = new LibraryRepository(factory);
        var categories = await repo.CategoriesAsync(default);
        var book = await repo.SaveBookAsync(new Book { ISBN = Random.Shared.NextInt64(1000000000000, 9999999999999).ToString(), Title = "Test " + suffix, Author = "Test Author", CategoryId = categories[0].Id, TotalCopies = copies }, user.Id, default);
        var member = await repo.SaveMemberAsync(new Member { MemberNumber = suffix[..25], FullName = "Test Reader", Email = suffix + "@reader.test", ExpiresOn = DateTime.UtcNow.AddYears(1) }, user.Id, default);
        return (repo, user.Id, book, member);
    }
    [SqlFact]
    public async Task Issue_return_and_duplicate_return_preserve_stock()
    {
        var (repo, actor, book, member) = await SeedAsync();
        var loan = await repo.IssueAsync(new() { BookId = book, MemberId = member, DueDate = DateTime.UtcNow.AddDays(14) }, new(), actor, default);
        await Assert.ThrowsAsync<BusinessException>(() => repo.IssueAsync(new() { BookId = book, MemberId = member, DueDate = DateTime.UtcNow.AddDays(14) }, new(), actor, default));
        await repo.ReturnAsync(loan, actor, default);
        await Assert.ThrowsAsync<BusinessException>(() => repo.ReturnAsync(loan, actor, default));
        var catalog = await repo.BooksAsync((await repo.BookAsync(book, default))!.Title, 1, false, default);
        Assert.Equal(1, Assert.Single(catalog.Items).AvailableCopies);
    }
    [SqlFact]
    public async Task Concurrent_last_copy_checkouts_allow_exactly_one()
    {
        var (repo, actor, book, member) = await SeedAsync();
        var second = await repo.SaveMemberAsync(new() { MemberNumber = Guid.NewGuid().ToString("N")[..25], FullName = "Second Reader", Email = Guid.NewGuid() + "@reader.test", ExpiresOn = DateTime.UtcNow.AddYears(1) }, actor, default);
        async Task<bool> TryIssue(int id)
        {
            try { await repo.IssueAsync(new() { BookId = book, MemberId = id, DueDate = DateTime.UtcNow.AddDays(14) }, new(), actor, default); return true; }
            catch (BusinessException) { return false; }
        }
        var results = await Task.WhenAll(TryIssue(member), TryIssue(second));
        Assert.Single(results, x => x);
        var catalog = await repo.BooksAsync((await repo.BookAsync(book, default))!.Title, 1, false, default);
        Assert.Equal(0, Assert.Single(catalog.Items).AvailableCopies);
    }
    [SqlFact]
    public async Task Stale_edit_and_archiving_on_loan_are_rejected()
    {
        var (repo, actor, bookId, member) = await SeedAsync();
        var book = (await repo.BookAsync(bookId, default))!;
        await repo.SaveBookAsync(book, actor, default);
        await Assert.ThrowsAsync<BusinessException>(() => repo.SaveBookAsync(book, actor, default));
        await repo.IssueAsync(new() { BookId = bookId, MemberId = member, DueDate = DateTime.UtcNow.AddDays(14) }, new(), actor, default);
        book = (await repo.BookAsync(bookId, default))!; book.IsArchived = true;
        await Assert.ThrowsAsync<BusinessException>(() => repo.SaveBookAsync(book, actor, default));
    }
    [SqlFact]
    public async Task Late_return_uses_snapshotted_rate_and_payment_is_unique()
    {
        var (repo, actor, book, member) = await SeedAsync();
        var loan = await repo.IssueAsync(new() { BookId = book, MemberId = member, DueDate = DateTime.UtcNow.AddDays(14) }, new() { DailyFine = 7.5m }, actor, default);
        await using var connection = await Factory().OpenAsync(default);
        // Dedicated test database only: simulate three elapsed calendar days.
        await connection.ExecuteAsync("UPDATE library.Loans SET DueDate=DATEADD(day,-3,CONVERT(date,SYSUTCDATETIME())) WHERE Id=@loan", new { loan });
        await repo.ReturnAsync(loan, actor, default);
        var result = await repo.LoansAsync(null, "returned", member, 1, default);
        Assert.Equal(22.5m, Assert.Single(result.Items).FineAmount);
        await repo.PayFineAsync(loan, actor, default);
        await Assert.ThrowsAsync<BusinessException>(() => repo.PayFineAsync(loan, actor, default));
        Assert.True(Assert.Single((await repo.LoansAsync(null, "returned", member, 1, default)).Items).IsPaid);
    }
    [SqlFact]
    public async Task Identity_store_detects_concurrent_updates()
    {
        var (_, actor, _, _) = await SeedAsync();
        var store = new DapperUserStore(Factory(), new IdentityErrorDescriber());
        var first = (await store.FindByIdAsync(actor, default))!;
        var stale = (await store.FindByIdAsync(actor, default))!;
        first.AccessFailedCount = 1;
        Assert.True((await store.UpdateAsync(first, default)).Succeeded);
        Assert.False((await store.UpdateAsync(stale, default)).Succeeded);
    }
}
