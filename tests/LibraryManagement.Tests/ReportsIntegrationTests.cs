using Dapper;
using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace LibraryManagement.Tests;
[Collection("MVC host")]
public sealed class ReportsIntegrationTests
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


    private static async Task<(int Book,int Member,int PaidLoan,int ActiveLoan)> Seed(ILibraryRepository repo,string actor)
    {
        var book=await Book(repo,actor,2);var suffix=Guid.NewGuid().ToString("N");var member=await repo.SaveMemberAsync(new(){MemberNumber=suffix[..25],FullName="Report Reader "+suffix,Email=suffix+"@report.test",ExpiresOn=DateTime.UtcNow.AddYears(1)},actor,default);
        var paid=await repo.IssueAsync(new(){BookId=book,MemberId=member,DueDate=DateTime.UtcNow.AddDays(5)},new(){DailyFine=7.25m},actor,default);await AgeLoan(paid,3);await repo.ReturnAsync(paid,actor,default);await repo.PayFineAsync(paid,actor,default);
        var active=await repo.IssueAsync(new(){BookId=book,MemberId=member,DueDate=DateTime.UtcNow.AddDays(5)},new(){DailyFine=5m},actor,default);await AgeLoan(active,2);return(book,member,paid,active);
    }
    [SqlFact] public async Task Reports_reconcile_stock_issue_return_overdue_member_and_payment_grains()
    {
        await using var host=Host();var(admin,_)=await Account(host,Roles.Admin);using var scope=host.Services.CreateScope();var repo=scope.ServiceProvider.GetRequiredService<ILibraryRepository>();var reports=scope.ServiceProvider.GetRequiredService<IReportService>();var s=await Seed(repo,admin.Id);
        async Task<ReportResult> Report(string key,string status="all",DateTime? from=null,DateTime? to=null)=>await reports.GetAsync(new(){Report=key,BookId=s.Book,MemberId=s.Member,Status=status,FromDate=from,ToDate=to},false,default);
        var stock=await Report("stock");Assert.Single(stock.Rows);Assert.Equal(2,stock.Totals.TotalCopies);Assert.Equal(1,stock.Totals.AvailableCopies);
        Assert.Equal(2,(await Report("issues")).Totals.TotalRecords);Assert.Equal(1,(await Report("issues","active")).Totals.TotalRecords);Assert.Equal(1,(await Report("issues","returned")).Totals.TotalRecords);Assert.Equal(1,(await Report("overdue")).Totals.TotalRecords);
        Assert.Equal(1,(await Report("returns","paid")).Totals.TotalRecords);Assert.Empty((await Report("returns","pending")).Rows);
        var members=await Report("members");Assert.Single(members.Rows);Assert.Equal(2,members.Totals.IssuedCount);Assert.Equal(1,members.Totals.ReturnedCount);Assert.Equal(1,members.Totals.OverdueCount);Assert.Equal(21.75m,members.Totals.CollectedAmount);
        var today=DateTime.UtcNow.Date;Assert.Empty((await Report("issues",from:today,to:today)).Rows);Assert.Single((await Report("returns",from:today,to:today)).Rows);Assert.Equal(21.75m,(await Report("collections",from:today,to:today)).Totals.CollectedAmount);
        var from=new DateTime(today.Year,today.Month,1).AddMonths(-2);var monthly=await Report("monthly",from:from,to:today);Assert.Equal(3,monthly.Rows.Count);Assert.Equal(2,monthly.Totals.IssuedCount);Assert.Equal(1,monthly.Totals.ReturnedCount);Assert.Equal(21.75m,monthly.Totals.CollectedAmount);Assert.All(monthly.Rows[0].Skip(1),c=>Assert.Equal(0m,c.Number));
        var paidOnly=await Report("monthly","paid",from,today);Assert.Equal(0,paidOnly.Totals.IssuedCount);Assert.Equal(21.75m,paidOnly.Totals.CollectedAmount);
        Assert.Empty((await reports.GetAsync(new(){Report="collections",BookId=s.Book,MemberId=int.MaxValue},false,default)).Rows);
    }
    [SqlFact] public async Task All_seven_report_pages_and_real_XLSX_PDF_downloads_work_for_librarians()
    {
        await using var host=Host();var(staff,password)=await Account(host,Roles.Librarian);using var scope=host.Services.CreateScope();var repo=scope.ServiceProvider.GetRequiredService<ILibraryRepository>();var s=await Seed(repo,staff.Id);using var client=Client(host);await Login(client,staff,password);
        foreach(var report in ReportCatalog.All)
        {
            var query=$"Report={report.Key}&BookId={s.Book}&MemberId={s.Member}";var page=await client.GetAsync("/Reports?"+query);Assert.Equal(HttpStatusCode.OK,page.StatusCode);Assert.Contains(report.Title,await page.Content.ReadAsStringAsync());Assert.Contains("no-store",page.Headers.CacheControl!.ToString());
            foreach(var format in new[]{"excel","pdf"})
            {
                var response=await client.GetAsync("/Reports/Export?"+query+"&format="+format);Assert.Equal(HttpStatusCode.OK,response.StatusCode);var bytes=await response.Content.ReadAsByteArrayAsync();Assert.True(bytes.Length>200);Assert.NotNull(response.Content.Headers.ContentDisposition);
                if(format=="pdf"){Assert.Equal("application/pdf",response.Content.Headers.ContentType!.MediaType);Assert.StartsWith("%PDF-",Encoding.ASCII.GetString(bytes,0,8));}
                else{Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",response.Content.Headers.ContentType!.MediaType);using var zip=new ZipArchive(new MemoryStream(bytes));Assert.NotNull(zip.GetEntry("xl/workbook.xml"));Assert.NotNull(zip.GetEntry("xl/worksheets/sheet1.xml"));}
                var directory=Environment.GetEnvironmentVariable("LIBRARY_TEST_ARTIFACTS");if(directory is not null){Directory.CreateDirectory(directory);await File.WriteAllBytesAsync(Path.Combine(directory,report.Key+(format=="pdf"?".pdf":".xlsx")),bytes);}
            }
        }
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("/Reports/Export?Report=stock&format=html")).StatusCode);
        Assert.Contains("From date cannot be after To date",await client.GetStringAsync("/Reports?Report=issues&FromDate=2026-10-23&ToDate=2026-10-22"));
    }
    [SqlFact] public async Task Exports_include_all_filtered_pages_and_text_is_not_an_Excel_formula()
    {
        await using var host=Host();var(admin,_)=await Account(host,Roles.Admin);using var scope=host.Services.CreateScope();var repo=scope.ServiceProvider.GetRequiredService<ILibraryRepository>();var reports=scope.ServiceProvider.GetRequiredService<IReportService>();var book=await Book(repo,admin.Id);var b=(await repo.BookAsync(book,default))!;b.Title="=HYPERLINK(unsafe) & <literal> "+new string('W',150);await repo.SaveBookAsync(b,admin.Id,default);var suffix=Guid.NewGuid().ToString("N");var member=await repo.SaveMemberAsync(new(){MemberNumber=suffix[..25],FullName="Export Reader "+suffix,Email=suffix+"@exports.test",ExpiresOn=DateTime.UtcNow.AddYears(1)},admin.Id,default);
        for(var i=0;i<25;i++){var loan=await repo.IssueAsync(new(){BookId=book,MemberId=member,DueDate=DateTime.UtcNow.AddDays(5)},new(),admin.Id,default);await repo.ReturnAsync(loan,admin.Id,default);}
        var filter=new ReportFilter{Report="issues",BookId=book,MemberId=member,Page=2};var screen=await reports.GetAsync(filter,false,default);Assert.Equal(25,screen.Totals.TotalRecords);Assert.Equal(5,screen.Rows.Count);
        var excel=await reports.ExportAsync(filter,"excel",default);using var zip=new ZipArchive(new MemoryStream(excel.Content));using var stream=zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();var xml=XDocument.Load(stream);XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        Assert.Empty(xml.Descendants(ns+"f"));Assert.Equal(25,xml.Descendants(ns+"row").Count(r=>int.Parse(r.Attribute("r")!.Value,CultureInfo.InvariantCulture) is >=6 and <=30));Assert.Contains(b.Title,xml.Descendants(ns+"t").Select(x=>x.Value));Assert.Contains(xml.Descendants(ns+"c"),c=>c.Attribute("r")?.Value=="A6"&&c.Attribute("t") is null);Assert.Contains(xml.Descendants(ns+"c"),c=>c.Attribute("r")?.Value=="F6"&&c.Attribute("s")?.Value=="3");
        var pdf=await reports.ExportAsync(filter,"pdf",default);Assert.True(Regex.Matches(Encoding.Latin1.GetString(pdf.Content),@"/Type /Page\b").Count>=2);
        var directory=Environment.GetEnvironmentVariable("LIBRARY_TEST_ARTIFACTS");if(directory is not null){Directory.CreateDirectory(directory);await File.WriteAllBytesAsync(Path.Combine(directory,"multipage.pdf"),pdf.Content);await File.WriteAllBytesAsync(Path.Combine(directory,"multipage.xlsx"),excel.Content);}
    }
    [SqlFact] public async Task Archived_books_and_members_remain_in_historical_reports()
    {
        await using var host=Host();var(admin,_)=await Account(host,Roles.Admin);using var scope=host.Services.CreateScope();var repo=scope.ServiceProvider.GetRequiredService<ILibraryRepository>();var reports=scope.ServiceProvider.GetRequiredService<IReportService>();var book=await Book(repo,admin.Id);var suffix=Guid.NewGuid().ToString("N");var member=await repo.SaveMemberAsync(new(){MemberNumber=suffix[..25],FullName="History Reader",Email=suffix+"@history.test",ExpiresOn=DateTime.UtcNow.AddYears(1)},admin.Id,default);
        var loan=await repo.IssueAsync(new(){BookId=book,MemberId=member,DueDate=DateTime.UtcNow.AddDays(5)},new(),admin.Id,default);await repo.ReturnAsync(loan,admin.Id,default);await repo.DeleteBookAsync(book,(await repo.BookAsync(book,default))!.Revision,admin.Id,default);await repo.DeleteMemberAsync(member,(await repo.MemberAsync(member,default))!.Revision,admin.Id,default);
        var stock=await reports.GetAsync(new(){Report="stock",BookId=book,MemberId=member,Status="archived"},false,default);Assert.Single(stock.Rows);Assert.Equal(1,stock.Totals.TotalCopies);Assert.Equal(0,stock.Totals.AvailableCopies);
        Assert.Single((await reports.GetAsync(new(){Report="returns",BookId=book,MemberId=member,Status="no-fine"},false,default)).Rows);Assert.Single((await reports.GetAsync(new(){Report="members",BookId=book,MemberId=member},false,default)).Rows);
    }
    [SqlFact] public async Task Members_and_anonymous_users_cannot_read_or_export_reports()
    {
        await using var host=Host();var(member,password)=await Account(host,Roles.Member);using var client=Client(host);await Login(client,member,password);foreach(var route in new[]{"/Reports","/Reports/Export?Report=collections&format=excel","/Reports/Export?Report=issues&format=pdf"})Assert.Contains("/Account/AccessDenied",(await client.GetAsync(route)).Headers.Location!.OriginalString);
        using var anonymous=Client(host);Assert.Contains("/Account/Login",(await anonymous.GetAsync("/Reports")).Headers.Location!.OriginalString);
    }
}
