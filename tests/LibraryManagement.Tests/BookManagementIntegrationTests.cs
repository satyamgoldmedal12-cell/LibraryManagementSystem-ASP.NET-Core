using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using LibraryManagement.Infrastructure.Images;
using SkiaSharp;
using Xunit;

namespace LibraryManagement.Tests;

[Collection("MVC host")]
public sealed class BookManagementIntegrationTests
{
    private static string Connection()
    {
        var connection = Environment.GetEnvironmentVariable("LIBRARY_TEST_CONNECTION")!;
        if (!new SqlConnectionStringBuilder(connection).InitialCatalog.EndsWith("_Test", StringComparison.Ordinal))
            throw new InvalidOperationException("Book tests require the dedicated _Test database.");
        return connection;
    }
    private static WebApplicationFactory<Program> Host() => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        b.UseEnvironment("Development").ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:LibraryDatabase"] = Connection() })));
    private static HttpClient Client(WebApplicationFactory<Program> host) => host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static string Token(string html) => Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    private static string Isbn()
    {
        var digits = Random.Shared.NextInt64(100000000000, 999999999999).ToString();
        return digits + ((10 - digits.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum() % 10) % 10);
    }
    private static async Task<(LibraryRepository Repo, string Actor, Book Book)> Seed()
    {
        var factory = new SqlConnectionFactory(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:LibraryDatabase"] = Connection() }).Build());
        var actor = new ApplicationUser { FullName = "Book Test", Role = Roles.Librarian, UserName = Guid.NewGuid().ToString("N") };
        actor.NormalizedUserName = actor.UserName.ToUpperInvariant(); actor.Email = actor.UserName + "@books.test"; actor.NormalizedEmail = actor.Email.ToUpperInvariant();
        Assert.True((await new DapperUserStore(factory, new IdentityErrorDescriber()).CreateAsync(actor, default)).Succeeded);
        var repo = new LibraryRepository(factory); var suffix = Guid.NewGuid().ToString("N");
        var id = await repo.SaveBookAsync(new Book { Title = "Book " + suffix, Author = "Author " + suffix, Publisher = "Publisher " + suffix, ISBN = Isbn(), CategoryId = 1 }, actor.Id, default);
        return (repo, actor.Id, (await repo.BookAsync(id, default))!);
    }
    private static BookCover Cover(SKColor? color = null) => new BookCoverProcessor().Process(new(BookCoverTests.Image(color: color), "cover.png"));
    [SqlFact]
    public async Task Search_supports_publisher_category_author_ISBN_and_literal_wildcards()
    {
        var (repo, actor, book) = await Seed(); var category = "Category-" + Guid.NewGuid().ToString("N");
        await repo.SaveCategoryAsync(new Category { Name = category }, actor, default);
        var suffix = Guid.NewGuid().ToString("N"); var literal = "100%_complete-" + suffix;
        book.CategoryId = (await repo.CategoriesAsync(default)).Single(c => c.Name == category).Id; book.Title += " " + literal;
        await repo.SaveBookAsync(book, actor, default);
        foreach (var term in new[] { book.Title, book.Author, book.Publisher!, category, book.ISBN, book.ISBN.Insert(3, "-"), literal })
            Assert.Contains((await repo.BooksAsync(term, 1, false, default)).Items, b => b.Id == book.Id);
        var decoy = await repo.SaveBookAsync(new Book { Title = "100XXcomplete-" + suffix, Author = "Decoy", ISBN = Isbn(), CategoryId = book.CategoryId }, actor, default);
        Assert.DoesNotContain((await repo.BooksAsync(literal, 1, false, default)).Items, b => b.Id == decoy);
        Assert.DoesNotContain((await repo.BooksAsync("NO_MATCH_%_" + Guid.NewGuid(), 1, false, default)).Items, b => b.Id == book.Id);
    }
    [SqlFact]
    public async Task Cover_replacement_and_removal_are_atomic_and_stale_updates_do_not_change_the_cover()
    {
        var (repo, actor, book) = await Seed(); var first = Cover();
        await repo.SaveBookWithCoverAsync(book, first, false, actor, default);
        Assert.Equal(first.Content, (await repo.BookCoverAsync(book.Id, default))!.Content);
        var stale = book; book = (await repo.BookAsync(book.Id, default))!; Assert.True(book.HasCover);
        book.Title += " updated"; await repo.SaveBookAsync(book, actor, default);
        await Assert.ThrowsAsync<BusinessException>(() => repo.SaveBookWithCoverAsync(stale, Cover(SKColors.Red), false, actor, default));
        Assert.Equal(first.Content, (await repo.BookCoverAsync(book.Id, default))!.Content);
        book = (await repo.BookAsync(book.Id, default))!; var replacement = Cover(SKColors.Red);
        await repo.SaveBookWithCoverAsync(book, replacement, false, actor, default);
        Assert.Equal(replacement.Content, (await repo.BookCoverAsync(book.Id, default))!.Content);
        book = (await repo.BookAsync(book.Id, default))!;
        await repo.SaveBookWithCoverAsync(book, null, true, actor, default);
        Assert.Null(await repo.BookCoverAsync(book.Id, default)); Assert.False((await repo.BookAsync(book.Id, default))!.HasCover);
    }
    [SqlFact]
    public async Task Delete_blocks_outstanding_loans_preserves_history_and_cover_and_allows_restore()
    {
        var (repo, actor, book) = await Seed(); await repo.SaveBookWithCoverAsync(book, Cover(), false, actor, default);
        var suffix = Guid.NewGuid().ToString("N");
        var member = await repo.SaveMemberAsync(new Member { MemberNumber = suffix[..25], FullName = "Book Reader", Email = suffix + "@reader.test", ExpiresOn = DateTime.UtcNow.AddYears(1) }, actor, default);
        var loan = await repo.IssueAsync(new IssueLoan { BookId = book.Id, MemberId = member, DueDate = DateTime.UtcNow.AddDays(7) }, new(), actor, default);
        book = (await repo.BookAsync(book.Id, default))!; Assert.Equal(0, book.AvailableCopies); Assert.Equal("All copies issued", book.Status);
        await Assert.ThrowsAsync<BusinessException>(() => repo.DeleteBookAsync(book.Id, book.Revision, actor, default));
        await repo.ReturnAsync(loan, actor, default); book = (await repo.BookAsync(book.Id, default))!; Assert.Equal("Available", book.Status);
        await repo.DeleteBookAsync(book.Id, book.Revision, actor, default); book = (await repo.BookAsync(book.Id, default))!;
        Assert.True(book.IsArchived); Assert.Equal("Archived", book.Status); Assert.True(book.HasCover);
        Assert.DoesNotContain((await repo.BooksAsync(book.Title, 1, false, default)).Items, b => b.Id == book.Id);
        Assert.Contains((await repo.BooksAsync(book.Title, 1, true, default)).Items, b => b.Id == book.Id);
        Assert.Contains((await repo.LoansAsync(book.Title, "returned", member, 1, default)).Items, l => l.Id == loan);
        Assert.NotNull(await repo.BookCoverAsync(book.Id, default)); book.IsArchived = false;
        await repo.SaveBookAsync(book, actor, default);
        Assert.False((await repo.BookAsync(book.Id, default))!.IsArchived);
    }
    [SqlFact]
    public async Task Stale_delete_is_rejected()
    {
        var (repo, actor, book) = await Seed(); var revision = book.Revision; await repo.SaveBookAsync(book, actor, default);
        await Assert.ThrowsAsync<BusinessException>(() => repo.DeleteBookAsync(book.Id, revision, actor, default));
        Assert.False((await repo.BookAsync(book.Id, default))!.IsArchived);
    }
    [SqlFact]
    public async Task Quantity_cannot_be_reduced_below_outstanding_loans()
    {
        var (repo, actor, book) = await Seed(); book.TotalCopies = 2; await repo.SaveBookAsync(book, actor, default);
        async Task<int> Member()
        {
            var suffix = Guid.NewGuid().ToString("N");
            return await repo.SaveMemberAsync(new Member { MemberNumber = suffix[..25], FullName = "Stock Reader", Email = suffix + "@stock.test", ExpiresOn = DateTime.UtcNow.AddYears(1) }, actor, default);
        }
        var first = await repo.IssueAsync(new IssueLoan { BookId = book.Id, MemberId = await Member(), DueDate = DateTime.UtcNow.AddDays(7) }, new(), actor, default);
        await repo.IssueAsync(new IssueLoan { BookId = book.Id, MemberId = await Member(), DueDate = DateTime.UtcNow.AddDays(7) }, new(), actor, default);
        book = (await repo.BookAsync(book.Id, default))!; book.TotalCopies = 1;
        await Assert.ThrowsAsync<BusinessException>(() => repo.SaveBookAsync(book, actor, default));
        Assert.Equal(2, (await repo.BookAsync(book.Id, default))!.TotalCopies);
        await repo.ReturnAsync(first, actor, default);
        await repo.SaveBookAsync(book, actor, default);
        Assert.Equal(0, (await repo.BookAsync(book.Id, default))!.AvailableCopies);
    }
    private static async Task<(ApplicationUser User, string Password)> Account(WebApplicationFactory<Program> host, string role)
    {
        using var scope = host.Services.CreateScope(); var email = Guid.NewGuid() + "@book-http.test"; var password = "Test!" + Guid.NewGuid().ToString("N") + "7aA";
        if (role == Roles.Member)
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<IMemberRegistrationService>().RegisterAsync(new RegisterModel { FullName = "Book Reader", Email = email, Password = password, ConfirmPassword = password })).Succeeded);
            return ((await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!, password);
        }
        var user = new ApplicationUser { FullName = "Book Staff", Email = email, UserName = email, Role = role };
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user, password)).Succeeded); return (user, password);
    }
    private static async Task Login(HttpClient client, ApplicationUser user, string password)
    {
        var page = await client.GetStringAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = user.Email!,
            ["Password"] = password,
            ["__RequestVerificationToken"] = Token(page)
        }))).StatusCode);
    }
    private static Dictionary<string, string> Fields(Book book, string token) => new()
    {
        ["Id"] = book.Id.ToString(),
        ["Revision"] = book.Revision.ToString(),
        ["Title"] = book.Title,
        ["Author"] = book.Author,
        ["ISBN"] = book.ISBN,
        ["Publisher"] = book.Publisher ?? "",
        ["CategoryId"] = book.CategoryId.ToString(),
        ["TotalCopies"] = book.TotalCopies.ToString(),
        ["IsArchived"] = book.IsArchived.ToString(),
        ["__RequestVerificationToken"] = token
    };
    private static MultipartFormDataContent Multipart(Dictionary<string, string> fields, byte[] bytes, string fileName)
    {
        var form = new MultipartFormDataContent(); foreach (var pair in fields) form.Add(new StringContent(pair.Value), pair.Key);
        var file = new ByteArrayContent(bytes); file.Headers.ContentType = new("image/png"); form.Add(file, "coverImage", fileName); return form;
    }
    [SqlFact]
    public async Task Librarian_can_upload_view_keep_and_remove_a_cover_using_MVC_forms()
    {
        await using var host = Host(); var (user, password) = await Account(host, Roles.Librarian); using var client = Client(host); await Login(client, user, password);
        using var scope = host.Services.CreateScope(); var repo = scope.ServiceProvider.GetRequiredService<ILibraryRepository>();
        var title = "HTTP Cover " + Guid.NewGuid(); var book = new Book { Title = title, Author = "Cover Author", Publisher = "Cover Press", ISBN = Isbn(), CategoryId = 1, TotalCopies = 3 };
        var page = await client.GetStringAsync("/Books/Edit"); var fields = Fields(book, Token(page)); fields["AvailableCopies"] = "99999"; fields["HasCover"] = "false";
        using var upload = Multipart(fields, BookCoverTests.Image(), "../../cover.png");
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Books/Edit/0", upload)).StatusCode);
        book = Assert.Single((await repo.BooksAsync(title, 1, false, default)).Items); Assert.True(book.HasCover); Assert.Equal(3, book.AvailableCopies);
        var cover = await client.GetAsync("/Books/Cover/" + book.Id); Assert.Equal(HttpStatusCode.OK, cover.StatusCode); Assert.Equal("image/jpeg", cover.Content.Headers.ContentType!.MediaType);
        Assert.Contains("Cover Press", await client.GetStringAsync("/Books/Details/" + book.Id));
        page = await client.GetStringAsync("/Books/Edit/" + book.Id); fields = Fields(book, Token(page)); fields["ISBN"] = book.ISBN.Insert(3, "-");
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Books/Edit/" + book.Id, new FormUrlEncodedContent(fields))).StatusCode);
        book = (await repo.BookAsync(book.Id, default))!; Assert.True(book.HasCover);
        page = await client.GetStringAsync("/Books/Edit/" + book.Id); fields = Fields(book, Token(page)); fields["removeCover"] = "true";
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Books/Edit/" + book.Id, new FormUrlEncodedContent(fields))).StatusCode);
        Assert.False((await repo.BookAsync(book.Id, default))!.HasCover); Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Books/Cover/" + book.Id)).StatusCode);
    }
    [SqlFact]
    public async Task Unsafe_upload_and_missing_CSRF_are_rejected_without_creating_a_book()
    {
        await using var host = Host(); var (user, password) = await Account(host, Roles.Admin); using var client = Client(host); await Login(client, user, password);
        using var scope = host.Services.CreateScope(); var repo = scope.ServiceProvider.GetRequiredService<ILibraryRepository>();
        var title = "Bad Cover " + Guid.NewGuid(); var book = new Book { Title = title, Author = "Test", ISBN = Isbn(), CategoryId = 1 };
        var page = await client.GetStringAsync("/Books/Edit");
        using (var forged = Multipart(Fields(book, ""), BookCoverTests.Image(), "cover.png"))
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Books/Edit/0", forged)).StatusCode);
        foreach (var bytes in new[] { System.Text.Encoding.UTF8.GetBytes("<svg onload='alert(1)'/>"), new byte[BookCoverLimits.MaxUploadBytes + 1] })
        {
            page = await client.GetStringAsync("/Books/Edit"); using var upload = Multipart(Fields(book, Token(page)), bytes, "cover.png");
            var result = await client.PostAsync("/Books/Edit/0", upload); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.DoesNotContain((await repo.BooksAsync(title, 1, false, default)).Items, b => b.Title == title);
        }
    }
    [SqlFact]
    public async Task Members_cannot_manage_books_or_read_archived_details_or_covers_and_delete_requires_CSRF()
    {
        var (repo, actor, book) = await Seed(); await repo.SaveBookWithCoverAsync(book, Cover(), false, actor, default); book = (await repo.BookAsync(book.Id, default))!;
        await using var host = Host(); var (admin, password) = await Account(host, Roles.Admin); using var staff = Client(host); await Login(staff, admin, password);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsync("/Books/Delete/" + book.Id, new FormUrlEncodedContent(new Dictionary<string, string> { ["revision"] = book.Revision.ToString() }))).StatusCode);
        var page = await staff.GetStringAsync("/Books/Delete/" + book.Id);
        Assert.Equal(HttpStatusCode.Redirect, (await staff.PostAsync("/Books/Delete/" + book.Id, new FormUrlEncodedContent(new Dictionary<string, string> { ["revision"] = book.Revision.ToString(), ["__RequestVerificationToken"] = Token(page) }))).StatusCode);
        var (member, memberPassword) = await Account(host, Roles.Member); using var client = Client(host); await Login(client, member, memberPassword);
        foreach (var route in new[] { "/Books/Edit/" + book.Id, "/Books/Delete/" + book.Id })
            Assert.Contains("/Account/AccessDenied", (await client.GetAsync(route)).Headers.Location!.OriginalString);
        foreach (var route in new[] { "/Books/Details/" + book.Id, "/Books/Cover/" + book.Id })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode);
        var hiddenCatalog = await client.GetStringAsync("/Books?includeArchived=true&search=" + Uri.EscapeDataString(book.Title));
        Assert.Contains("No books found", hiddenCatalog);
        Assert.DoesNotContain("href=\"/Books/Details/" + book.Id + "\"", hiddenCatalog);
        var memberToken = Token(await client.GetStringAsync("/Books"));
        foreach (var route in new[] { "/Books/Edit/" + book.Id, "/Books/Delete/" + book.Id })
            Assert.Contains("/Account/AccessDenied", (await client.PostAsync(route, new FormUrlEncodedContent(Fields(book, memberToken)))).Headers.Location!.OriginalString);
        using var anonymous = Client(host); Assert.Contains("/Account/Login", (await anonymous.GetAsync("/Books/Cover/" + book.Id)).Headers.Location!.OriginalString);
    }
}
