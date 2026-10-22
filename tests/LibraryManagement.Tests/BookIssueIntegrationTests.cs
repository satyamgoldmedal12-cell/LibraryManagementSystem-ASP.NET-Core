using Dapper;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace LibraryManagement.Tests;
[Collection("MVC host")]
public sealed class BookIssueIntegrationTests
{
    private static WebApplicationFactory<Program> Host()
    {
        var connection = Environment.GetEnvironmentVariable("LIBRARY_TEST_CONNECTION")!;
        if (!new SqlConnectionStringBuilder(connection).InitialCatalog.EndsWith("_Test", StringComparison.Ordinal)) throw new InvalidOperationException("Dedicated _Test database required.");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development").ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:LibraryDatabase"] = connection })));
    }
    private static HttpClient Client(WebApplicationFactory<Program> host) => host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static string Token(string html) => Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    private static async Task<(ApplicationUser User, string Password)> Account(WebApplicationFactory<Program> host, string role)
    {
        using var scope = host.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = Guid.NewGuid() + "@member-test.example"; var password = "Test!" + Guid.NewGuid().ToString("N") + "7aA";
        if (role == Roles.Member) { Assert.True((await scope.ServiceProvider.GetRequiredService<IMemberRegistrationService>().RegisterAsync(new RegisterModel { FullName = "Member Reader", Email = email, Password = password, ConfirmPassword = password })).Succeeded); return ((await users.FindByEmailAsync(email))!, password); }
        var user = new ApplicationUser { FullName = "Member Staff", Email = email, UserName = email, Role = role }; Assert.True((await users.CreateAsync(user, password)).Succeeded); return (user, password);
    }
    private static async Task<HttpResponseMessage> Login(HttpClient client, ApplicationUser user, string password)
    {
        var page = await client.GetStringAsync("/Account/Login"); return await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = user.Email!, ["Password"] = password, ["__RequestVerificationToken"] = Token(page) }));
    }

    private static async Task<int> Book(ILibraryRepository repo,string actor,int copies=1) => await repo.SaveBookAsync(new() { Title="Issue test "+Guid.NewGuid(),Author="Issue Author",ISBN=Random.Shared.NextInt64(1000000000000,9999999999999).ToString(),CategoryId=(await repo.CategoriesAsync(default))[0].Id,TotalCopies=copies },actor,default);
    private static Dictionary<string,string> Fields(int book,int member,string token) => new() { ["BookId"]=book.ToString(),["MemberId"]=member.ToString(),["DueDate"]=DateTime.UtcNow.AddDays(5).ToString("yyyy-MM-dd"),["__RequestVerificationToken"]=token,["IssuedAt"]="2000-01-01",["DailyFine"]="0",["MaxLoans"]="999" };
    [SqlFact] public async Task Staff_checkout_records_authoritative_dates_receipt_and_history_and_rejects_duplicates()
    {
        await using var host=Host();var (staff,password)=await Account(host,Roles.Librarian);var (reader,_)=await Account(host,Roles.Member);
        using var scope=host.Services.CreateScope();var repo=scope.ServiceProvider.GetRequiredService<ILibraryRepository>();var book=await Book(repo,staff.Id,2);using var client=Client(host);await Login(client,staff,password);
        var url=$"/Loans/Issue?bookId={book}&memberId={reader.MemberId}";var page=await client.GetStringAsync(url);
        Assert.Contains("id=\"issue-date\"",page);Assert.Contains("readonly",page);Assert.Contains("name=\"BookId\"",page);Assert.Contains("name=\"DueDate\"",page);Assert.DoesNotContain("name=\"IssuedAt\"",page);
        Assert.Empty((await repo.LoansAsync(null,"all",reader.MemberId,1,default)).Items);
        var check=JsonDocument.Parse(await client.GetStringAsync($"/Loans/Availability?bookId={book}&memberId={reader.MemberId}"));Assert.True(check.RootElement.GetProperty("canIssue").GetBoolean());Assert.Equal(2,check.RootElement.GetProperty("availableCopies").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/Loans/Issue",new FormUrlEncodedContent(Fields(book,reader.MemberId!.Value,"")))).StatusCode);
        var before=DateTime.UtcNow.AddSeconds(-2);var response=await client.PostAsync("/Loans/Issue",new FormUrlEncodedContent(Fields(book,reader.MemberId!.Value,Token(page))));Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);Assert.StartsWith("/Loans/Receipt/",response.Headers.Location!.OriginalString);
        var loan=Assert.Single((await repo.LoansAsync(null,"all",reader.MemberId,1,default)).Items);var receipt=(await repo.IssueReceiptAsync(loan.Id,default))!;Assert.InRange(receipt.IssuedAt,before,DateTime.UtcNow.AddSeconds(2));Assert.Equal(5m,receipt.DailyFine);Assert.Equal(staff.FullName,receipt.IssuedByName);Assert.Equal(DateTime.UtcNow.Date.AddDays(5),receipt.DueDate);
        Assert.Contains("Issue receipt",await client.GetStringAsync(response.Headers.Location));
        var duplicate=await client.PostAsync("/Loans/Issue",new FormUrlEncodedContent(Fields(book,reader.MemberId.Value,Token(page))));Assert.Equal(HttpStatusCode.OK,duplicate.StatusCode);Assert.Contains("already has this book",await duplicate.Content.ReadAsStringAsync());Assert.Single((await repo.LoansAsync(null,"all",reader.MemberId,1,default)).Items);
        Assert.Contains(receipt.BookTitle,await client.GetStringAsync("/Loans/History?search="+Uri.EscapeDataString(receipt.BookTitle)));
        await repo.ReturnAsync(loan.Id,staff.Id,default);Assert.NotNull((await repo.IssueReceiptAsync(loan.Id,default))!.ReturnedAt);
        await repo.IssueAsync(new() { BookId=book,MemberId=reader.MemberId.Value,DueDate=DateTime.UtcNow.AddDays(5) },new(),staff.Id,default);Assert.Equal(2,(await repo.LoansAsync(null,"all",reader.MemberId,1,default)).Total);
    }
    [SqlFact] public async Task Stock_is_rechecked_after_preview_and_member_receipts_are_private()
    {
        await using var host=Host();var (staff,password)=await Account(host,Roles.Admin);var (one,onePass)=await Account(host,Roles.Member);var (two,_)=await Account(host,Roles.Member);using var scope=host.Services.CreateScope();var repo=scope.ServiceProvider.GetRequiredService<ILibraryRepository>();var issues=scope.ServiceProvider.GetRequiredService<IBookIssueService>();var book=await Book(repo,staff.Id);using var client=Client(host);await Login(client,staff,password);
        Assert.True((await issues.AssessAsync(book,two.MemberId!.Value,null,default)).CanIssue);
        var own=await repo.IssueAsync(new() { BookId=book,MemberId=one.MemberId!.Value,DueDate=DateTime.UtcNow.AddDays(5) },new(),staff.Id,default);
        var page=await client.GetStringAsync($"/Loans/Issue?bookId={book}&memberId={two.MemberId}");var failed=await client.PostAsync("/Loans/Issue",new FormUrlEncodedContent(Fields(book,two.MemberId.Value,Token(page))));Assert.Equal(HttpStatusCode.OK,failed.StatusCode);Assert.Contains("No copies are available",await failed.Content.ReadAsStringAsync());Assert.Empty((await repo.LoansAsync(null,"all",two.MemberId,1,default)).Items);
        var lookup=JsonDocument.Parse(await client.GetStringAsync("/Books/Lookup?search="+Uri.EscapeDataString((await repo.BookAsync(book,default))!.Title)));Assert.True(lookup.RootElement[0].GetProperty("disabled").GetBoolean());
        var foreignBook=await Book(repo,staff.Id);var foreign=await repo.IssueAsync(new() { BookId=foreignBook,MemberId=two.MemberId.Value,DueDate=DateTime.UtcNow.AddDays(5) },new(),staff.Id,default);
        using var member=Client(host);await Login(member,one,onePass);Assert.Equal(HttpStatusCode.OK,(await member.GetAsync("/Loans/Receipt/"+own)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await member.GetAsync("/Loans/Receipt/"+foreign)).StatusCode);
        foreach(var route in new[]{"/Loans/Issue","/Loans/Availability","/Loans/History"})Assert.Contains("/Account/AccessDenied",(await member.GetAsync(route)).Headers.Location!.OriginalString);
        await Assert.ThrowsAsync<BusinessException>(()=>repo.IssueAsync(new() { BookId=foreignBook,MemberId=one.MemberId.Value,DueDate=DateTime.UtcNow.AddDays(5) },new(),one.Id,default));
    }
    [SqlFact] public async Task Concurrent_same_member_book_requests_create_only_one_active_issue()
    {
        await using var host=Host();var (staff,_)=await Account(host,Roles.Librarian);var (member,_)=await Account(host,Roles.Member);using var scope=host.Services.CreateScope();var repo=scope.ServiceProvider.GetRequiredService<ILibraryRepository>();var book=await Book(repo,staff.Id,2);
        async Task<bool> TryIssue(){try{await repo.IssueAsync(new(){BookId=book,MemberId=member.MemberId!.Value,DueDate=DateTime.UtcNow.AddDays(5)},new(),staff.Id,default);return true;}catch(BusinessException){return false;}}
        Assert.Single(await Task.WhenAll(TryIssue(),TryIssue()),x=>x);Assert.Single((await repo.LoansAsync(null,"all",member.MemberId,1,default)).Items);Assert.Equal(1,(await repo.BookAsync(book,default))!.AvailableCopies);
    }
}
