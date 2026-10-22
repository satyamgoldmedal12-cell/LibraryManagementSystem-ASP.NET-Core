using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LibraryManagement.Tests;

[Collection("MVC host")]
public sealed class HttpIntegrationTests
{
    private static WebApplicationFactory<Program> Factory()
    {
        var connection = Environment.GetEnvironmentVariable("LIBRARY_TEST_CONNECTION")!;
        if (!new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection).InitialCatalog.EndsWith("_Test", StringComparison.Ordinal))
            throw new InvalidOperationException("HTTP integration tests require the dedicated _Test database.");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:LibraryDatabase"] = connection })));
    }
    private static HttpClient Client(WebApplicationFactory<Program> app) => app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static string Token(string html) => Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    private static async Task Login(HttpClient client, string email, string password)
    {
        var page = await client.GetStringAsync("/Account/Login");
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Email"] = email, ["Password"] = password, ["__RequestVerificationToken"] = Token(page), ["ReturnUrl"] = "https://untrusted.example" }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }
    [SqlFact]
    public async Task Anonymous_routes_require_authentication_and_login_requires_antiforgery()
    {
        await using var app = Factory(); using var client = Client(app);
        var protectedPage = await client.GetAsync("/Books");
        Assert.Equal(HttpStatusCode.Redirect, protectedPage.StatusCode);
        Assert.Contains("/Account/Login", protectedPage.Headers.Location!.OriginalString);
        var login = await client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.NotEmpty(Token(await login.Content.ReadAsStringAsync()));
        Assert.True(login.Headers.Contains("Content-Security-Policy"));
        var invalid = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = "nobody@example.test", ["Password"] = "InvalidPassword!123" }));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
    [SqlFact]
    public async Task Member_cannot_read_other_loans_or_use_staff_routes()
    {
        await using var app = Factory(); using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var repo = scope.ServiceProvider.GetRequiredService<ILibraryRepository>();
        var suffix = Guid.NewGuid().ToString("N"); var password = "Test!" + Guid.NewGuid().ToString("N") + "7aA";
        var admin = new ApplicationUser { FullName = "HTTP Test Admin", Email = suffix + "@admin.test", UserName = suffix + "@admin.test", Role = Roles.Admin };
        Assert.True((await users.CreateAsync(admin, password)).Succeeded);
        var member = await repo.SaveMemberAsync(new() { MemberNumber = suffix[..25], FullName = "Member A", Email = suffix + "@member.test", ExpiresOn = DateTime.UtcNow.AddYears(1) }, admin.Id, default);
        var other = await repo.SaveMemberAsync(new() { MemberNumber = "B" + suffix[..24], FullName = "Member B", Email = suffix + "@other.test", ExpiresOn = DateTime.UtcNow.AddYears(1) }, admin.Id, default);
        var account = new ApplicationUser { FullName = "HTTP Test Member", Email = suffix + "@member.test", UserName = suffix + "@member.test", Role = Roles.Member, MemberId = member };
        Assert.True((await users.CreateAsync(account, password)).Succeeded);
        var category = (await repo.CategoriesAsync(default))[0].Id;
        var ownTitle = "Own " + suffix; var otherTitle = "Private " + suffix;
        async Task<int> Book(string title) => await repo.SaveBookAsync(new() { Title = title, Author = "Test", ISBN = Random.Shared.NextInt64(1000000000000, 9999999999999).ToString(), CategoryId = category }, admin.Id, default);
        var ownBook = await Book(ownTitle); var otherBook = await Book(otherTitle);
        await repo.IssueAsync(new() { BookId = ownBook, MemberId = member, DueDate = DateTime.UtcNow.AddDays(5) }, new(), admin.Id, default);
        await repo.IssueAsync(new() { BookId = otherBook, MemberId = other, DueDate = DateTime.UtcNow.AddDays(5) }, new(), admin.Id, default);
        using var client = Client(app); await Login(client, account.Email!, password);
        var page = await client.GetStringAsync("/Loans?status=all&memberId=" + other);
        Assert.Contains(ownTitle, page); Assert.DoesNotContain(otherTitle, page);
        foreach (var route in new[] { "/Members", "/Users", "/Books/Edit", "/Loans/Issue", "/Home/Audit" })
        {
            var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/AccessDenied", response.Headers.Location!.OriginalString);
        }
    }
    [SqlFact]
    public async Task Administrator_pages_render_and_form_save_requires_antiforgery()
    {
        await using var app = Factory(); using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var suffix = Guid.NewGuid().ToString("N"); var password = "Test!" + suffix + "7aA";
        var admin = new ApplicationUser { FullName = "Render Test Admin", Email = suffix + "@admin.test", UserName = suffix + "@admin.test", Role = Roles.Admin };
        Assert.True((await users.CreateAsync(admin, password)).Succeeded);
        using var client = Client(app); await Login(client, admin.Email!, password);
        foreach (var route in new[] { "/", "/Books", "/Books/Edit", "/Members", "/Members/Edit", "/Loans", "/Loans/Issue", "/Categories", "/Users", "/Users/Create", "/Home/Audit", "/Account/ChangePassword" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(route)).StatusCode);
        var form = await client.GetStringAsync("/Books/Edit");
        var data = new Dictionary<string, string> { ["Id"] = "0", ["Title"] = "HTTP Save " + suffix, ["Author"] = "HTTP Test", ["ISBN"] = "9780201633610", ["CategoryId"] = "1", ["TotalCopies"] = "2" };
        var missing = await client.PostAsync("/Books/Edit/0", new FormUrlEncodedContent(data));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        // Use a unique, valid ISBN-13, including its checksum.
        var digits = Random.Shared.NextInt64(100000000000, 999999999999).ToString();
        var checksum = (10 - digits.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum() % 10) % 10;
        data["ISBN"] = digits + checksum; data["__RequestVerificationToken"] = Token(form);
        var saved = await client.PostAsync("/Books/Edit/0", new FormUrlEncodedContent(data));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Contains("HTTP Save " + suffix, await client.GetStringAsync("/Books?search=" + suffix));
    }
}
