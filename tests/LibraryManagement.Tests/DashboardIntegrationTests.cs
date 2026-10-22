using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LibraryManagement.Tests;

[Collection("MVC host")]
public sealed class DashboardIntegrationTests
{
    private static SqlConnectionFactory Factory()
    {
        var value = Environment.GetEnvironmentVariable("LIBRARY_TEST_CONNECTION")!;
        if (!new SqlConnectionStringBuilder(value).InitialCatalog.EndsWith("_Test", StringComparison.Ordinal))
            throw new InvalidOperationException("Dashboard tests require the dedicated _Test database.");
        return new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:LibraryDatabase"] = value }).Build());
    }

    [SqlFact]
    public async Task Cards_reconcile_stock_returns_members_and_paid_fines_at_correct_grains()
    {
        var factory = Factory(); var repo = new LibraryRepository(factory); var baseline = await repo.DashboardAsync(default);
        var suffix = Guid.NewGuid().ToString("N");
        var actor = new ApplicationUser
        {
            FullName = "Dashboard Test",
            Role = Roles.Librarian,
            UserName = suffix,
            NormalizedUserName = suffix.ToUpperInvariant(),
            Email = suffix + "@dashboard.test",
            NormalizedEmail = suffix.ToUpperInvariant() + "@DASHBOARD.TEST"
        };
        Assert.True((await new DapperUserStore(factory, new IdentityErrorDescriber()).CreateAsync(actor, default)).Succeeded);
        var category = (await repo.CategoriesAsync(default))[0].Id;
        async Task<int> Book(int copies) => await repo.SaveBookAsync(new Book
        { ISBN = Random.Shared.NextInt64(1000000000000, 9999999999999).ToString(), Title = "Dashboard " + Guid.NewGuid(), Author = "Test", CategoryId = category, TotalCopies = copies }, actor.Id, default);
        var book = await Book(5); var archived = await Book(3);
        var member = await repo.SaveMemberAsync(new Member { FullName = "Dashboard Reader", MemberNumber = suffix[..25], Email = suffix + "@reader.test", ExpiresOn = DateTime.UtcNow.AddYears(1) }, actor.Id, default);
        await repo.SaveMemberAsync(new Member
        {
            FullName = "Inactive Reader",
            MemberNumber = "I" + suffix[..24],
            Email = suffix + "@inactive.test",
            JoinedOn = DateTime.UtcNow.AddYears(-1),
            ExpiresOn = DateTime.UtcNow.AddDays(-1),
            IsActive = false
        }, actor.Id, default);
        async Task<int> Issue(int bookId) => await repo.IssueAsync(new IssueLoan { BookId = bookId, MemberId = member, DueDate = DateTime.UtcNow.AddDays(14) }, new LendingOptions { DailyFine = 7.5m }, actor.Id, default);
        await using var connection = await factory.OpenAsync(default);
        var first = await Issue(book);
        await connection.ExecuteAsync("UPDATE library.Loans SET IssuedAt=DATEADD(day,9,DATEADD(month,-1,DATEFROMPARTS(YEAR(SYSUTCDATETIME()),MONTH(SYSUTCDATETIME()),1))), DueDate=DATEADD(day,-3,CONVERT(date,SYSUTCDATETIME())) WHERE Id=@Id", new { Id = first });
        await repo.ReturnAsync(first, actor.Id, default); await repo.PayFineAsync(first, actor.Id, default);
        var unpaid = await Issue(book);
        await connection.ExecuteAsync("UPDATE library.Loans SET IssuedAt=DATEADD(day,-2,SYSUTCDATETIME()),DueDate=DATEADD(day,-1,CONVERT(date,SYSUTCDATETIME())) WHERE Id=@Id", new { Id = unpaid });
        await repo.ReturnAsync(unpaid, actor.Id, default); // This 7.50 fine is NOT a collected payment.
        var historical = await Issue(archived); await repo.ReturnAsync(historical, actor.Id, default);
        await connection.ExecuteAsync("UPDATE library.Loans SET IssuedAt=DATEADD(day,9,DATEADD(month,-4,DATEFROMPARTS(YEAR(SYSUTCDATETIME()),MONTH(SYSUTCDATETIME()),1))),ReturnedAt=DATEADD(day,11,DATEADD(month,-3,DATEFROMPARTS(YEAR(SYSUTCDATETIME()),MONTH(SYSUTCDATETIME()),1))) WHERE Id=@Id", new { Id = historical });
        var retired = (await repo.BookAsync(archived, default))!; retired.IsArchived = true; await repo.SaveBookAsync(retired, actor.Id, default);
        var outstanding = await Issue(book);
        await connection.ExecuteAsync("UPDATE library.Loans SET IssuedAt=DATEADD(day,-14,SYSUTCDATETIME()),DueDate=DATEADD(day,-1,CONVERT(date,SYSUTCDATETIME())) WHERE Id=@Id", new { Id = outstanding });
        var dashboard = await repo.DashboardAsync(default);
        Assert.Equal(baseline.TotalBooks + 5, dashboard.TotalBooks);
        Assert.Equal(baseline.AvailableBooks + 4, dashboard.AvailableBooks);
        Assert.Equal(baseline.IssuedBooks + 1, dashboard.IssuedBooks);
        Assert.Equal(baseline.OverdueBooks + 1, dashboard.OverdueBooks);
        Assert.Equal(baseline.ReturnedBooks + 3, dashboard.ReturnedBooks);
        Assert.Equal(baseline.TotalMembers + 2, dashboard.TotalMembers);
        Assert.Equal(baseline.ActiveMembers + 1, dashboard.ActiveMembers);
        Assert.Equal(baseline.TotalFineCollected + 22.5m, dashboard.TotalFineCollected);
        Assert.Equal(dashboard.TotalBooks, dashboard.AvailableBooks + dashboard.IssuedBooks);
        Assert.InRange(dashboard.OverdueBooks, 0, dashboard.IssuedBooks);
        var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        MonthlyCirculation Current(Dashboard d, int offset) => Assert.Single(d.MonthlyActivity, x => x.Month == currentMonth.AddMonths(offset));
        Assert.Equal(Current(baseline, -1).IssuedBooks + 1, Current(dashboard, -1).IssuedBooks);
        Assert.Equal(Current(baseline, -4).IssuedBooks + 1, Current(dashboard, -4).IssuedBooks);
        Assert.Equal(Current(baseline, -3).ReturnedBooks + 1, Current(dashboard, -3).ReturnedBooks);
        // Checkouts/returns use their own event dates, rather than the issue month for both.
        Assert.Equal(Current(baseline, 0).ReturnedBooks + 2, Current(dashboard, 0).ReturnedBooks);
    }

    [SqlFact]
    public async Task History_contains_six_ordered_utc_months_and_matches_raw_events()
    {
        var factory = Factory(); var dashboard = await new LibraryRepository(factory).DashboardAsync(default);
        Assert.Equal(6, dashboard.MonthlyActivity.Count);
        var currentMonth = new DateTime(dashboard.RefreshedAt.Year, dashboard.RefreshedAt.Month, 1);
        await using var connection = await factory.OpenAsync(default);
        for (var index = 0; index < 6; index++)
        {
            var month = dashboard.MonthlyActivity[index]; var next = month.Month.AddMonths(1);
            Assert.Equal(currentMonth.AddMonths(index - 5), month.Month);
            var issued = await connection.ExecuteScalarAsync<long>("SELECT COUNT_BIG(*) FROM library.Loans WHERE IssuedAt>=@Start AND IssuedAt<@Next AND IssuedAt<=@AsOf",
                new { Start = month.Month, Next = next, AsOf = dashboard.RefreshedAt });
            var returned = await connection.ExecuteScalarAsync<long>("SELECT COUNT_BIG(*) FROM library.Loans WHERE ReturnedAt>=@Start AND ReturnedAt<@Next AND ReturnedAt<=@AsOf",
                new { Start = month.Month, Next = next, AsOf = dashboard.RefreshedAt });
            Assert.Equal(issued, month.IssuedBooks); Assert.Equal(returned, month.ReturnedBooks);
        }
    }

    [SqlFact]
    public async Task Overview_renders_seven_cards_and_local_charts_but_members_get_no_staff_data()
    {
        var connectionString = Environment.GetEnvironmentVariable("LIBRARY_TEST_CONNECTION")!;
        _ = Factory(); // Enforce the dedicated test database guard.
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:LibraryDatabase"] = connectionString })));
        using var scope = app.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var suffix = Guid.NewGuid().ToString("N"); var password = "Test!" + suffix + "7aA";
        var staff = new ApplicationUser { FullName = "Dashboard Staff", Role = Roles.Librarian, Email = suffix + "@staff.test", UserName = suffix + "@staff.test" };
        Assert.True((await users.CreateAsync(staff, password)).Succeeded);
        HttpClient Client() => app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        async Task SignIn(HttpClient client, string email)
        {
            var page = await client.GetStringAsync("/Account/Login");
            var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Email"] = email, ["Password"] = password, ["__RequestVerificationToken"] = token }))).StatusCode);
        }
        using var client = Client(); await SignIn(client, staff.Email!);
        var html = await client.GetStringAsync("/");
        foreach (var label in new[] { "Total Books", "Available Books", "Issued Books", "Returned Books", "Overdue Books", "Total Members", "Total Fine Collected" })
            Assert.Contains(label, html);
        Assert.Equal(7, Regex.Matches(html, "data-metric=").Count);
        Assert.Contains("/lib/chartjs/chart.umd.min.js", html); Assert.Contains("/js/dashboard.js", html);
        Assert.Contains("Monthly issues and returns", html);
        Assert.DoesNotContain("+<div", html);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/lib/chartjs/chart.umd.min.js")).StatusCode);
        var email = suffix + "@member.test";
        Assert.True((await scope.ServiceProvider.GetRequiredService<IMemberRegistrationService>().RegisterAsync(new RegisterModel
        { FullName = "Dashboard Reader", Email = email, Password = password, ConfirmPassword = password })).Succeeded);
        using var member = Client(); await SignIn(member, email);
        var response = await member.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); Assert.Equal("/Loans", response.Headers.Location!.OriginalString);
        Assert.DoesNotContain("dashboard-data", await response.Content.ReadAsStringAsync());
    }
}
