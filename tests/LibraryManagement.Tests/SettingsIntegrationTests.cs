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
public sealed class SettingsIntegrationTests
{
    private static string ConnectionString()
    {
        var value = Environment.GetEnvironmentVariable("LIBRARY_TEST_CONNECTION")!;
        if (!new SqlConnectionStringBuilder(value).InitialCatalog.EndsWith("_Test", StringComparison.Ordinal))
            throw new InvalidOperationException("Dedicated _Test database required.");
        return value;
    }
    private static WebApplicationFactory<Program> Host() => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        b.UseEnvironment("Development").ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:LibraryDatabase"] = ConnectionString() })));
    private static HttpClient Client(WebApplicationFactory<Program> host) => host.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static string Token(string html) => Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    private static async Task<(ApplicationUser User, string Password)> Account(WebApplicationFactory<Program> host, string role)
    {
        using var scope = host.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = Guid.NewGuid() + "@settings.example"; var password = "Test!" + Guid.NewGuid().ToString("N") + "7aA";
        if (role == Roles.Member)
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<IMemberRegistrationService>().RegisterAsync(
                new() { FullName = "Settings Reader", Email = email, Password = password, ConfirmPassword = password })).Succeeded);
            return ((await users.FindByEmailAsync(email))!, password);
        }
        var user = new ApplicationUser { FullName = "Settings Admin", Email = email, UserName = email, Role = role };
        Assert.True((await users.CreateAsync(user, password)).Succeeded); return (user, password);
    }
    private static async Task Login(HttpClient client, ApplicationUser user, string password)
    {
        var page = await client.GetStringAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Email"] = user.Email!, ["Password"] = password, ["__RequestVerificationToken"] = Token(page) }))).StatusCode);
    }
    private static Dictionary<string, string> Fields(LibrarySettings value, string token) => new()
    {
        ["LibraryName"] = value.LibraryName, ["Address"] = value.Address ?? "", ["Email"] = value.Email ?? "", ["Phone"] = value.Phone ?? "",
        ["MaxActiveLoans"] = value.MaxActiveLoans.ToString(), ["DefaultIssueDays"] = value.DefaultIssueDays.ToString(),
        ["DailyFine"] = value.DailyFine.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["MembershipDurationMonths"] = value.MembershipDurationMonths.ToString(), ["Revision"] = value.Revision.ToString(), ["__RequestVerificationToken"] = token
    };
    private static async Task<int> Book(ILibraryRepository repo, string actor) => await repo.SaveBookAsync(new()
    { Title = "Settings Book " + Guid.NewGuid(), Author = "Policy Author", ISBN = Random.Shared.NextInt64(1000000000000, 9999999999999).ToString(),
        CategoryId = (await repo.CategoriesAsync(default))[0].Id, TotalCopies = 2 }, actor, default);
    private static async Task<dynamic> Snapshot(SqlConnection sql) => await sql.QuerySingleAsync("SELECT * FROM library.FinePolicy WHERE Id=1");
    private static Task Restore(SqlConnection sql, object snapshot) => sql.ExecuteAsync(@"
        UPDATE library.FinePolicy SET LibraryName=@LibraryName,Address=@Address,Email=@Email,Phone=@Phone,MaxActiveLoans=@MaxActiveLoans,
        DefaultIssueDays=@DefaultIssueDays,DailyFine=@DailyFine,MembershipDurationMonths=@MembershipDurationMonths,
        CreatedBy=@CreatedBy,ModifiedAt=@ModifiedAt,ModifiedBy=@ModifiedBy,Revision=Revision+1 WHERE Id=1", snapshot);

    [SqlFact]
    public async Task Admin_form_persists_validates_encodes_and_rejects_stale_shared_fine_revision()
    {
        await using var host = Host(); var (admin, password) = await Account(host, Roles.Admin);
        using var scope = host.Services.CreateScope(); var settings = scope.ServiceProvider.GetRequiredService<ILibrarySettingsService>();
        await using var sql = new SqlConnection(ConnectionString()); object previous = await Snapshot(sql);
        try
        {
            using var client = Client(host); await Login(client, admin, password);
            var page = await client.GetStringAsync("/Settings"); Assert.Contains("Library information", page); Assert.Contains("Membership duration (months)", page);
            var model = await settings.GetAsync(default); var data = Fields(model, Token(page));
            data.Remove("__RequestVerificationToken"); Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Settings", new FormUrlEncodedContent(data))).StatusCode);
            var incomplete = Fields(model, Token(page)); incomplete.Remove("DefaultIssueDays");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Settings", new FormUrlEncodedContent(incomplete))).StatusCode);
            foreach (var (field, value) in new[] { ("LibraryName", " "), ("MaxActiveLoans", "0"), ("DefaultIssueDays", "91"),
                ("MembershipDurationMonths", "121"), ("DailyFine", "0.001"), ("Email", "invalid-email"), ("Phone", "invalid-phone") })
            {
                var invalid = Fields(model, Token(page)); invalid[field] = value;
                Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/Settings", new FormUrlEncodedContent(invalid))).StatusCode);
                Assert.Equal(model.Revision, (await settings.GetAsync(default)).Revision);
            }
            model.LibraryName = "  City <script>alert(1)</script> Library  "; model.Address = "  Main Road  ";
            model.Email = "desk@example.test"; model.Phone = "+91 9876543210"; model.MaxActiveLoans = 3;
            model.DefaultIssueDays = 9; model.DailyFine = 8.75m; model.MembershipDurationMonths = 6;
            data = Fields(model, Token(page)); data["ModifiedByName"] = "Injected";
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Settings", new FormUrlEncodedContent(data))).StatusCode);
            var saved = await settings.GetAsync(default);
            Assert.Equal("City <script>alert(1)</script> Library", saved.LibraryName); Assert.Equal("Main Road", saved.Address);
            Assert.Equal(3, saved.MaxActiveLoans); Assert.Equal(9, saved.DefaultIssueDays); Assert.Equal(8.75m, saved.DailyFine); Assert.Equal(6, saved.MembershipDurationMonths);
            Assert.Equal(admin.FullName, saved.ModifiedByName); Assert.NotNull(saved.ModifiedAt);
            var html = await client.GetStringAsync("/Settings"); Assert.Contains("&lt;script&gt;", html); Assert.DoesNotContain("<script>alert(1)</script>", html);
            Assert.Contains("Library settings changed", await (await client.PostAsync("/Settings", new FormUrlEncodedContent(data))).Content.ReadAsStringAsync());
            var circulation = scope.ServiceProvider.GetRequiredService<ICirculationService>(); var fine = await circulation.PolicyAsync(default);
            Assert.Equal(saved.DailyFine, fine.DailyFine); Assert.Equal(saved.Revision, fine.Revision);
            fine.DailyFine = 2.25m; await circulation.SavePolicyAsync(fine, admin.Id, default);
            saved.Phone = "9876543210"; await Assert.ThrowsAsync<BusinessException>(() => settings.SaveAsync(saved, admin.Id, default));
            Assert.Equal(2.25m, (await settings.GetAsync(default)).DailyFine);
            Assert.Equal(1, await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM library.AuditEvents WHERE ActorId=@Id AND Action='Library settings updated'", new { admin.Id }));
        }
        finally { await Restore(sql, previous); }
    }

    [SqlFact]
    public async Task Saved_policy_controls_registration_member_form_checkout_limit_and_new_loan_rate()
    {
        await using var host = Host(); var (admin, password) = await Account(host, Roles.Admin); var (existing, _) = await Account(host, Roles.Member);
        using var scope = host.Services.CreateScope(); var settings = scope.ServiceProvider.GetRequiredService<ILibrarySettingsService>();
        var repo = scope.ServiceProvider.GetRequiredService<ILibraryRepository>(); var issues = scope.ServiceProvider.GetRequiredService<IBookIssueService>();
        await using var sql = new SqlConnection(ConnectionString()); object previous = await Snapshot(sql);
        try
        {
            var originalExpiry = (await repo.MemberAsync(existing.MemberId!.Value, default))!.ExpiresOn;
            var oldBook = await Book(repo, admin.Id); var oldLoan = await repo.IssueAsync(new() { BookId = oldBook, MemberId = existing.MemberId.Value, DueDate = DateTime.UtcNow.AddDays(5) }, new(), admin.Id, default);
            var oldRate = (await repo.IssueReceiptAsync(oldLoan, default))!.DailyFine;
            var model = await settings.GetAsync(default); model.MaxActiveLoans = 1; model.DefaultIssueDays = 7; model.DailyFine = 7.25m; model.MembershipDurationMonths = 3;
            await settings.SaveAsync(model, admin.Id, default);
            Assert.Equal(originalExpiry, (await repo.MemberAsync(existing.MemberId.Value, default))!.ExpiresOn);
            Assert.Equal(oldRate, (await repo.IssueReceiptAsync(oldLoan, default))!.DailyFine);
            using var anonymous = Client(host); var page = await anonymous.GetStringAsync("/Account/Register");
            var email = Guid.NewGuid() + "@duration.example"; var pass = "Test!" + Guid.NewGuid().ToString("N") + "7aA";
            Assert.Equal(HttpStatusCode.Redirect, (await anonymous.PostAsync("/Account/Register", new FormUrlEncodedContent(new Dictionary<string,string>
            { ["FullName"] = "Duration Reader", ["Email"] = email, ["Password"] = pass, ["ConfirmPassword"] = pass,
              ["ExpiresOn"] = "2099-01-01", ["Role"] = "Admin", ["__RequestVerificationToken"] = Token(page) }))).StatusCode);
            var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!;
            Assert.Equal(Roles.Member, user.Role); var member = (await repo.MemberAsync(user.MemberId!.Value, default))!;
            Assert.Equal(DateTime.UtcNow.Date.AddMonths(3), member.ExpiresOn);
            using var client = Client(host); await Login(client, admin, password);
            Assert.Contains("value=\"" + DateTime.UtcNow.Date.AddMonths(3).ToString("yyyy-MM-dd") + "\"", await client.GetStringAsync("/Members/Edit"));
            var first = await Book(repo, admin.Id); var second = await Book(repo, admin.Id);
            var assessment = await issues.AssessAsync(first, member.Id, null, default);
            Assert.True(assessment.CanIssue); Assert.Equal(1, assessment.MaximumBooks); Assert.Equal(DateTime.UtcNow.Date.AddDays(7), assessment.DueDate);
            var loan = await repo.IssueAsync(new() { BookId = first, MemberId = member.Id, DueDate = assessment.DueDate },
                new() { MaxActiveLoans = 100, DailyFine = 999m }, admin.Id, default);
            Assert.Equal(7.25m, (await repo.IssueReceiptAsync(loan, default))!.DailyFine);
            Assert.False((await issues.AssessAsync(second, member.Id, null, default)).CanIssue);
            await Assert.ThrowsAsync<BusinessException>(() => repo.IssueAsync(new() { BookId = second, MemberId = member.Id, DueDate = assessment.DueDate },
                new() { MaxActiveLoans = 100 }, admin.Id, default));
            await sql.ExecuteAsync("UPDATE library.Loans SET IssuedAt=DATEADD(day,-30,SYSUTCDATETIME()),DueDate=DATEADD(day,-4,CONVERT(date,SYSUTCDATETIME())) WHERE Id=@loan", new { loan });
            await repo.ReturnAsync(loan, admin.Id, default);
            Assert.Equal(29m, (await scope.ServiceProvider.GetRequiredService<ICirculationService>().DetailsAsync(loan, default))!.FineAmount);
        }
        finally { await Restore(sql, previous); }
    }

    [SqlFact]
    public async Task Only_active_admin_can_read_or_write_settings_and_sql_rejects_invalid_policy()
    {
        await using var host = Host(); var (admin, _) = await Account(host, Roles.Admin);
        using var scope = host.Services.CreateScope(); var settings = scope.ServiceProvider.GetRequiredService<ILibrarySettingsService>();
        var model = await settings.GetAsync(default);
        foreach (var role in new[] { Roles.Librarian, Roles.Member })
        {
            var (user, password) = await Account(host, role); using var client = Client(host); await Login(client, user, password);
            Assert.Contains("/Account/AccessDenied", (await client.GetAsync("/Settings")).Headers.Location!.OriginalString);
            var token = Token(await client.GetStringAsync("/Loans"));
            Assert.Contains("/Account/AccessDenied", (await client.PostAsync("/Settings", new FormUrlEncodedContent(Fields(model, token)))).Headers.Location!.OriginalString);
            await Assert.ThrowsAsync<BusinessException>(() => settings.SaveAsync(model, user.Id, default));
        }
        using var anonymous = Client(host); Assert.Contains("/Account/Login", (await anonymous.GetAsync("/Settings")).Headers.Location!.OriginalString);
        await using var sql = new SqlConnection(ConnectionString());
        var args = new { model.LibraryName, model.Address, model.Email, model.Phone, MaxActiveLoans = 101, model.DefaultIssueDays,
            model.DailyFine, model.MembershipDurationMonths, model.Revision, ActorId = admin.Id };
        var invalid = await Assert.ThrowsAsync<SqlException>(() => sql.ExecuteAsync("library.Settings_Save", args, commandType: System.Data.CommandType.StoredProcedure));
        Assert.Equal(50001, invalid.Number); Assert.Equal(model.Revision, (await settings.GetAsync(default)).Revision);
        await sql.ExecuteAsync("UPDATE library.Users SET IsActive=0 WHERE Id=@Id", new { admin.Id });
        await Assert.ThrowsAsync<BusinessException>(() => settings.SaveAsync(model, admin.Id, default));
    }

    [SqlFact]
    public async Task Concurrent_settings_saves_commit_one_revision_and_one_audit_event()
    {
        await using var host = Host(); var (admin, _) = await Account(host, Roles.Admin);
        using var scope = host.Services.CreateScope(); var settings = scope.ServiceProvider.GetRequiredService<ILibrarySettingsService>();
        await using var sql = new SqlConnection(ConnectionString()); object previous = await Snapshot(sql);
        try
        {
            var left = await settings.GetAsync(default); var right = await settings.GetAsync(default);
            left.DefaultIssueDays = 11; right.DefaultIssueDays = 17;
            async Task<bool> Save(LibrarySettings value) { try { await settings.SaveAsync(value, admin.Id, default); return true; } catch (BusinessException) { return false; } }
            Assert.Single(await Task.WhenAll(Save(left), Save(right)), x => x);
            var current = await settings.GetAsync(default); Assert.Equal(left.Revision + 1, current.Revision); Assert.Contains(current.DefaultIssueDays, new[] { 11, 17 });
            Assert.Equal(1, await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM library.AuditEvents WHERE ActorId=@Id AND Action='Library settings updated'", new { admin.Id }));
        }
        finally { await Restore(sql, previous); }
    }
}
