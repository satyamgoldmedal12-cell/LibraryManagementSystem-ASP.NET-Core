using Dapper;
using System.Net;
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
public sealed class ReturnFineIntegrationTests
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

    private static SqlConnection Connection()
    {
        var value=Environment.GetEnvironmentVariable("LIBRARY_TEST_CONNECTION")!;
        if(!new SqlConnectionStringBuilder(value).InitialCatalog.EndsWith("_Test",StringComparison.Ordinal)) throw new InvalidOperationException("Dedicated test database required.");
        return new(value);
    }

    private static async Task AgeLoan(int loan,int days)
    {
        await using var sql=Connection();
        await sql.ExecuteAsync("UPDATE library.Loans SET IssuedAt=DATEADD(day,-30,SYSUTCDATETIME()),DueDate=DATEADD(day,-@days,CONVERT(date,SYSUTCDATETIME())) WHERE Id=@loan",new{loan,days});
    }

    private static FormUrlEncodedContent Command(string token) => new(new Dictionary<string,string>{["__RequestVerificationToken"]=token,["ReturnedAt"]="2000-01-01",["FineAmount"]="0.01",["Amount"]="0.01",["PaidAt"]="2000-01-01",["LateDays"]="0"});

    [SqlFact]
    public async Task Return_preview_is_read_only_and_server_finalizes_time_fine_stock_and_history()
    {
        await using var host=Host();var(staff,password)=await Account(host,Roles.Librarian);var(reader,_)=await Account(host,Roles.Member);
        using var scope=host.Services.CreateScope();var repo=scope.ServiceProvider.GetRequiredService<ILibraryRepository>();var circulation=scope.ServiceProvider.GetRequiredService<ICirculationService>();var book=await Book(repo,staff.Id);
        var loan=await repo.IssueAsync(new(){BookId=book,MemberId=reader.MemberId!.Value,DueDate=DateTime.UtcNow.AddDays(5)},new(){DailyFine=7.25m},staff.Id,default);
        await AgeLoan(loan,3);using var client=Client(host);await Login(client,staff,password);
        var page=await client.GetStringAsync("/Returns/Confirm/"+loan);Assert.Contains("Estimated fine",page);Assert.Contains("21.75",page);
        Assert.Null((await circulation.DetailsAsync(loan,default))!.ReturnedAt);Assert.Equal(0,(await repo.BookAsync(book,default))!.AvailableCopies);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/Returns/Confirm/"+loan,Command(""))).StatusCode);
        var before=DateTime.UtcNow.AddSeconds(-2);var response=await client.PostAsync("/Returns/Confirm/"+loan,Command(Token(page)));Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        var returned=(await circulation.DetailsAsync(loan,default))!;Assert.Equal(3,returned.LateDays);Assert.Equal(21.75m,returned.FineAmount);Assert.Equal(staff.FullName,returned.ReturnedByName);Assert.InRange(returned.ReturnedAt!.Value,before,DateTime.UtcNow.AddSeconds(2));Assert.Equal(1,returned.AvailableCopies);
        Assert.Contains("Return receipt",await client.GetStringAsync(response.Headers.Location));
        var repeated=await client.PostAsync("/Returns/Confirm/"+loan,Command(Token(page)));Assert.Equal(HttpStatusCode.OK,repeated.StatusCode);Assert.Contains("already been returned",await repeated.Content.ReadAsStringAsync());Assert.Equal(returned.ReturnedAt,(await circulation.DetailsAsync(loan,default))!.ReturnedAt);
        var history=await circulation.ListAsync("returns",new(){MemberId=reader.MemberId},default);Assert.Equal(loan,Assert.Single(history.Records.Items).Id);
        Assert.Contains(returned.Title,await client.GetStringAsync("/Returns?MemberId="+reader.MemberId));
        await Assert.ThrowsAsync<BusinessException>(()=>repo.ReturnAsync(loan,reader.Id,default));
    }

    [SqlFact]
    public async Task On_time_returns_have_zero_late_days_and_release_one_copy_each()
    {
        await using var host=Host();var(staff,_)=await Account(host,Roles.Librarian);var(one,_)=await Account(host,Roles.Member);var(two,_)=await Account(host,Roles.Member);using var scope=host.Services.CreateScope();var repo=scope.ServiceProvider.GetRequiredService<ILibraryRepository>();var circulation=scope.ServiceProvider.GetRequiredService<ICirculationService>();var book=await Book(repo,staff.Id,2);
        var first=await repo.IssueAsync(new(){BookId=book,MemberId=one.MemberId!.Value,DueDate=DateTime.UtcNow.AddDays(5)},new(),staff.Id,default);
        var second=await repo.IssueAsync(new(){BookId=book,MemberId=two.MemberId!.Value,DueDate=DateTime.UtcNow.AddDays(5)},new(),staff.Id,default);
        await AgeLoan(second,0);
        await repo.ReturnAsync(first,staff.Id,default);var returned=(await circulation.DetailsAsync(first,default))!;Assert.Equal(0,returned.LateDays);Assert.Equal(0m,returned.FineAmount);Assert.Equal(1,returned.AvailableCopies);
        await repo.ReturnAsync(second,staff.Id,default);returned=(await circulation.DetailsAsync(second,default))!;Assert.Equal(0,returned.LateDays);Assert.Equal(0m,returned.FineAmount);Assert.Equal(2,returned.AvailableCopies);await Assert.ThrowsAsync<BusinessException>(()=>repo.PayFineAsync(second,staff.Id,default));
    }

    [SqlFact]
    public async Task Payment_uses_full_finalized_fine_and_moves_pending_to_paid_with_receipt()
    {
        await using var host=Host();var(staff,password)=await Account(host,Roles.Librarian);var(reader,_)=await Account(host,Roles.Member);
        using var scope=host.Services.CreateScope();var repo=scope.ServiceProvider.GetRequiredService<ILibraryRepository>();var circulation=scope.ServiceProvider.GetRequiredService<ICirculationService>();var book=await Book(repo,staff.Id);
        var loan=await repo.IssueAsync(new(){BookId=book,MemberId=reader.MemberId!.Value,DueDate=DateTime.UtcNow.AddDays(5)},new(){DailyFine=5m},staff.Id,default);await AgeLoan(loan,2);
        Assert.Equal(10m,(await circulation.ListAsync("fines",new(){MemberId=reader.MemberId,Status="accruing"},default)).Totals.AccruingFine);
        await Assert.ThrowsAsync<BusinessException>(()=>repo.PayFineAsync(loan,staff.Id,default));await repo.ReturnAsync(loan,staff.Id,default);
        Assert.Equal(10m,(await circulation.ListAsync("fines",new(){MemberId=reader.MemberId,Status="pending"},default)).Totals.PendingFine);
        using var client=Client(host);await Login(client,staff,password);var page=await client.GetStringAsync("/Fines/Pay/"+loan);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/Fines/Pay/"+loan,Command(""))).StatusCode);
        var response=await client.PostAsync("/Fines/Pay/"+loan,Command(Token(page)));Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        var paid=(await circulation.DetailsAsync(loan,default))!;Assert.NotNull(paid.PaymentId);Assert.Equal(10m,paid.PaidAmount);Assert.Equal(staff.FullName,paid.ReceivedByName);Assert.Equal(DateTime.UtcNow.Date,paid.PaidAt!.Value.Date);
        Assert.Contains("Fine payment receipt",await client.GetStringAsync(response.Headers.Location));Assert.Empty((await circulation.ListAsync("fines",new(){MemberId=reader.MemberId,Status="pending"},default)).Records.Items);Assert.Equal(10m,(await circulation.ListAsync("fines",new(){MemberId=reader.MemberId,Status="paid"},default)).Totals.PaidFine);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsync("/Fines/Pay/"+loan,Command(Token(page)))).StatusCode);await Assert.ThrowsAsync<BusinessException>(()=>repo.PayFineAsync(loan,reader.Id,default));
        await using var sql=Connection();Assert.Equal(1,await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM library.Payments WHERE LoanId=@loan",new{loan}));
    }

    [SqlFact]
    public async Task Concurrent_returns_and_payments_each_commit_exactly_once()
    {
        await using var host=Host();var(staff,_)=await Account(host,Roles.Librarian);var(reader,_)=await Account(host,Roles.Member);using var scope=host.Services.CreateScope();var repo=scope.ServiceProvider.GetRequiredService<ILibraryRepository>();var book=await Book(repo,staff.Id);
        var loan=await repo.IssueAsync(new(){BookId=book,MemberId=reader.MemberId!.Value,DueDate=DateTime.UtcNow.AddDays(5)},new(),staff.Id,default);await AgeLoan(loan,1);
        async Task<bool> Try(Func<Task> action){try{await action();return true;}catch(BusinessException){return false;}}
        Assert.Single(await Task.WhenAll(Try(()=>repo.ReturnAsync(loan,staff.Id,default)),Try(()=>repo.ReturnAsync(loan,staff.Id,default))),x=>x);Assert.Equal(1,(await repo.BookAsync(book,default))!.AvailableCopies);
        Assert.Single(await Task.WhenAll(Try(()=>repo.PayFineAsync(loan,staff.Id,default)),Try(()=>repo.PayFineAsync(loan,staff.Id,default))),x=>x);
        await using var sql=Connection();Assert.Equal(1,await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM library.Payments WHERE LoanId=@loan",new{loan}));
    }

    [SqlFact]
    public async Task Collection_report_dates_member_book_pagination_and_totals_include_archived_history()
    {
        await using var host=Host();var(staff,password)=await Account(host,Roles.Admin);var(reader,_)=await Account(host,Roles.Member);using var scope=host.Services.CreateScope();var repo=scope.ServiceProvider.GetRequiredService<ILibraryRepository>();var circulation=scope.ServiceProvider.GetRequiredService<ICirculationService>();var book=await Book(repo,staff.Id);
        for(var i=0;i<13;i++) { var loan=await repo.IssueAsync(new(){BookId=book,MemberId=reader.MemberId!.Value,DueDate=DateTime.UtcNow.AddDays(5)},new(){DailyFine=2.5m},staff.Id,default);await AgeLoan(loan,3);await repo.ReturnAsync(loan,staff.Id,default);await repo.PayFineAsync(loan,staff.Id,default); }
        var archived=(await repo.BookAsync(book,default))!;archived.IsArchived=true;await repo.SaveBookAsync(archived,staff.Id,default);
        var filter=new CirculationFilter(){BookId=book,MemberId=reader.MemberId,FromDate=DateTime.UtcNow.Date,ToDate=DateTime.UtcNow.Date};var first=await circulation.ListAsync("collections",filter,default);Assert.Equal(13,first.Records.Total);Assert.Equal(12,first.Records.Items.Count);Assert.Equal(97.5m,first.Totals.PaidFine);
        filter.Page=2;var second=await circulation.ListAsync("collections",filter,default);Assert.Single(second.Records.Items);Assert.Equal(97.5m,second.Totals.PaidFine);Assert.DoesNotContain(first.Records.Items,x=>x.Id==second.Records.Items[0].Id);
        filter.FromDate=DateTime.UtcNow.Date.AddDays(1);filter.ToDate=filter.FromDate;Assert.Empty((await circulation.ListAsync("collections",filter,default)).Records.Items);
        using var client=Client(host);await Login(client,staff,password);var report=await client.GetStringAsync($"/Fines/Collection?BookId={book}&MemberId={reader.MemberId}");Assert.Contains("97.50",report);Assert.Contains("13 payments",report);Assert.Contains("BookId="+book,report);
        Assert.Contains("From date cannot be after To date",await client.GetStringAsync("/Fines/Collection?FromDate=2026-10-22&ToDate=2026-10-21"));
        Assert.Contains("No records",await client.GetStringAsync("/Returns?FromDate=invalid"));
    }

    [SqlFact]
    public async Task Only_admin_can_set_persistent_fine_rate_and_existing_loans_keep_their_rate()
    {
        await using var host=Host();var(admin,password)=await Account(host,Roles.Admin);var(librarian,staffPass)=await Account(host,Roles.Librarian);var(reader,_)=await Account(host,Roles.Member);using var scope=host.Services.CreateScope();var repo=scope.ServiceProvider.GetRequiredService<ILibraryRepository>();var circulation=scope.ServiceProvider.GetRequiredService<ICirculationService>();var issues=scope.ServiceProvider.GetRequiredService<IBookIssueService>();
        await using var sql=Connection();var previous=await sql.ExecuteScalarAsync<decimal?>("SELECT DailyFine FROM library.FinePolicy WHERE Id=1");
        try
        {
            var oldBook=await Book(repo,admin.Id);var oldLoan=await repo.IssueAsync(new(){BookId=oldBook,MemberId=reader.MemberId!.Value,DueDate=DateTime.UtcNow.AddDays(5)},new(){DailyFine=5m},admin.Id,default);
            var policy=await circulation.PolicyAsync(default);using var client=Client(host);await Login(client,admin,password);var page=await client.GetStringAsync("/Fines/Settings");
            Dictionary<string,string> Fields(string rate,string token) => new(){["DailyFine"]=rate,["Revision"]=policy.Revision.ToString(),["__RequestVerificationToken"]=token};
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/Fines/Settings",new FormUrlEncodedContent(Fields("7.25","")))).StatusCode);
            foreach(var invalid in new[]{"-1","1000.01","0.001"}) Assert.Equal(HttpStatusCode.OK,(await client.PostAsync("/Fines/Settings",new FormUrlEncodedContent(Fields(invalid,Token(page))))).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Fines/Settings",new FormUrlEncodedContent(Fields("7.25",Token(page))))).StatusCode);
            Assert.Equal(7.25m,(await circulation.PolicyAsync(default)).DailyFine);Assert.Equal(5m,(await repo.IssueReceiptAsync(oldLoan,default))!.DailyFine);
            Assert.Contains("Fine settings changed",await (await client.PostAsync("/Fines/Settings",new FormUrlEncodedContent(Fields("8",Token(page))))).Content.ReadAsStringAsync());
            var nextBook=await Book(repo,admin.Id);Assert.Equal(7.25m,(await issues.AssessAsync(nextBook,reader.MemberId.Value,null,default)).DailyFine);var nextLoan=await repo.IssueAsync(new(){BookId=nextBook,MemberId=reader.MemberId.Value,DueDate=DateTime.UtcNow.AddDays(5)},new(){DailyFine=999m},admin.Id,default);Assert.Equal(7.25m,(await repo.IssueReceiptAsync(nextLoan,default))!.DailyFine);
            policy=await circulation.PolicyAsync(default);await Assert.ThrowsAsync<BusinessException>(()=>circulation.SavePolicyAsync(policy,librarian.Id,default));policy.DailyFine=0;await circulation.SavePolicyAsync(policy,admin.Id,default);
            var freeBook=await Book(repo,admin.Id);var free=await repo.IssueAsync(new(){BookId=freeBook,MemberId=reader.MemberId.Value,DueDate=DateTime.UtcNow.AddDays(5)},new(),admin.Id,default);await AgeLoan(free,2);await repo.ReturnAsync(free,admin.Id,default);Assert.Equal(0m,(await circulation.DetailsAsync(free,default))!.FineAmount);
            using var staff=Client(host);await Login(staff,librarian,staffPass);Assert.Contains("/Account/AccessDenied",(await staff.GetAsync("/Fines/Settings")).Headers.Location!.OriginalString);
        }
        finally { await sql.ExecuteAsync("UPDATE library.FinePolicy SET DailyFine=@previous,Revision=Revision+1 WHERE Id=1",new{previous}); }
    }

    [SqlFact]
    public async Task Member_cannot_access_staff_return_fine_reports_settings_or_mutations()
    {
        await using var host=Host();var(reader,password)=await Account(host,Roles.Member);using var client=Client(host);await Login(client,reader,password);
        foreach(var route in new[]{"/Returns","/Returns/Confirm/1","/Returns/Details/1","/Fines","/Fines/Collection","/Fines/Settings","/Fines/Pay/1","/Fines/Receipt/1"})Assert.Contains("/Account/AccessDenied",(await client.GetAsync(route)).Headers.Location!.OriginalString);
        var token=Token(await client.GetStringAsync("/Loans"));foreach(var route in new[]{"/Returns/Confirm/1","/Fines/Pay/1","/Fines/Settings"})Assert.Contains("/Account/AccessDenied",(await client.PostAsync(route,Command(token))).Headers.Location!.OriginalString);
        using var anonymous=Client(host);Assert.Contains("/Account/Login",(await anonymous.GetAsync("/Fines/Collection")).Headers.Location!.OriginalString);
    }
}
