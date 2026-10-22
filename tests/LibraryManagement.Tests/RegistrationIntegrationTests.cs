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
public sealed class RegistrationIntegrationTests
{
    private static WebApplicationFactory<Program> Factory()
    {
        var connection = Environment.GetEnvironmentVariable("LIBRARY_TEST_CONNECTION")!;
        if (!new SqlConnectionStringBuilder(connection).InitialCatalog.EndsWith("_Test", StringComparison.Ordinal))
            throw new InvalidOperationException("Registration tests require the dedicated _Test database.");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:LibraryDatabase"] = connection })));
    }
    private static HttpClient Client(WebApplicationFactory<Program> app) => app.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static string Token(string html) => Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    private static Dictionary<string, string> Input(string? email = null)
    {
        var password = "Test!" + Guid.NewGuid().ToString("N") + "7aA";
        return new()
        {
            ["FullName"] = "New Reader",
            ["Email"] = email ?? Guid.NewGuid() + "@registration.test",
            ["Password"] = password,
            ["ConfirmPassword"] = password
        };
    }
    private static async Task<HttpResponseMessage> Register(HttpClient client, Dictionary<string, string> data)
    {
        data["__RequestVerificationToken"] = Token(await client.GetStringAsync("/Account/Register"));
        return await client.PostAsync("/Account/Register", new FormUrlEncodedContent(data));
    }

    [SqlFact]
    public async Task Anonymous_registration_is_linked_and_requires_antiforgery()
    {
        await using var app = Factory(); using var client = Client(app);
        Assert.Contains("href=\"/Account/Register\"", await client.GetStringAsync("/Account/Login"));
        var form = await client.GetStringAsync("/Account/Register");
        Assert.Contains("Confirm password", form);
        Assert.DoesNotContain("name=\"Role\"", form);
        Assert.NotEmpty(Token(form));
        var response = await client.PostAsync("/Account/Register", new FormUrlEncodedContent(Input()));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [SqlFact]
    public async Task Registration_creates_a_member_and_cannot_grant_staff_access()
    {
        await using var app = Factory(); using var client = Client(app);
        var data = Input();
        data["Role"] = "Admin"; data["MemberId"] = "1"; data["EmailConfirmed"] = "true";
        var response = await Register(client, data);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.OriginalString);
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(data["Email"]);
        Assert.NotNull(user);
        Assert.Equal(Roles.Member, user.Role);
        Assert.False(user.EmailConfirmed);
        Assert.True(user.LockoutEnabled);
        Assert.NotNull(user.MemberId);
        Assert.NotEqual(data["Password"], user.PasswordHash);
        Assert.True(await users.CheckPasswordAsync(user, data["Password"]));
        var member = await scope.ServiceProvider.GetRequiredService<ILibraryRepository>().MemberAsync(user.MemberId.Value, default);
        Assert.NotNull(member);
        Assert.StartsWith("LIB-", member.MemberNumber);
        Assert.Equal(data["Email"], member.Email);
        Assert.True(member.IsActive);
        Assert.Equal(DateTime.UtcNow.Date.AddYears(1), member.ExpiresOn);
        var loginPage = await client.GetStringAsync("/Account/Login");
        Assert.Contains("Registration successful", loginPage);
        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Email"] = data["Email"], ["Password"] = data["Password"], ["__RequestVerificationToken"] = Token(loginPage) }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Loans")).StatusCode);
        var staff = await client.GetAsync("/Users");
        Assert.Equal(HttpStatusCode.Redirect, staff.StatusCode);
        Assert.Contains("/Account/AccessDenied", staff.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Account/Register")).StatusCode);
    }

    [SqlFact]
    public async Task Invalid_registration_does_not_create_an_account()
    {
        await using var app = Factory(); using var client = Client(app); using var scope = app.Services.CreateScope();
        var cases = new[] { ("FullName", ""), ("Email", "invalid-email"),
            ("Password", "onlylowercaseletters"), ("ConfirmPassword", "Different!Password123") };
        foreach (var (field, value) in cases)
        {
            var data = Input(); data[field] = value;
            if (field == "Password") data["ConfirmPassword"] = value;
            var result = await Register(client, data);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var html = await result.Content.ReadAsStringAsync();
            Assert.True(html.Contains("field-validation-error") || html.Contains("validation-summary-errors"), "Validation feedback should be visible.");
            Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(data["Email"]));
        }
    }

    [SqlFact]
    public async Task Duplicate_signup_keeps_one_account_and_membership()
    {
        await using var app = Factory(); using var client = Client(app);
        var data = Input();
        Assert.Equal(HttpStatusCode.Redirect, (await Register(client, data)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Register(client, data)).StatusCode);
        using var scope = app.Services.CreateScope();
        await using var connection = await scope.ServiceProvider.GetRequiredService<SqlConnectionFactory>().OpenAsync(default);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM library.Members WHERE Email=@Email", new { Email = data["Email"] }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM library.Users WHERE NormalizedEmail=@Email", new { Email = data["Email"].ToUpperInvariant() }));
    }

    [SqlFact]
    public async Task Existing_membership_cannot_be_claimed_through_public_signup()
    {
        await using var app = Factory(); using var client = Client(app); using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var data = Input();
        var actor = new ApplicationUser { FullName = "Test Administrator", Role = Roles.Admin, Email = Guid.NewGuid() + "@admin.test" };
        actor.UserName = actor.Email;
        Assert.True((await users.CreateAsync(actor, data["Password"])).Succeeded);
        var repo = scope.ServiceProvider.GetRequiredService<ILibraryRepository>();
        var member = await repo.SaveMemberAsync(new Member
        {
            FullName = "Existing Member",
            MemberNumber = Guid.NewGuid().ToString("N")[..25],
            Email = data["Email"],
            ExpiresOn = DateTime.UtcNow.AddYears(1)
        }, actor.Id, default);
        var result = await Register(client, data);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("already registered", await result.Content.ReadAsStringAsync());
        Assert.Null(await users.FindByEmailAsync(data["Email"]));
        Assert.Equal("Existing Member", (await repo.MemberAsync(member, default))!.FullName);
    }

    [SqlFact]
    public async Task Failed_account_insert_rolls_back_the_new_membership()
    {
        await using var app = Factory(); using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var data = Input();
        var actor = new ApplicationUser { FullName = "Test Administrator", Role = Roles.Admin, Email = Guid.NewGuid() + "@admin.test" };
        actor.UserName = actor.Email;
        Assert.True((await users.CreateAsync(actor, data["Password"])).Succeeded);
        await using var connection = await scope.ServiceProvider.GetRequiredService<SqlConnectionFactory>().OpenAsync(default);
        // Force failure after membership insertion by reusing a user ID. The public form cannot supply this value.
        var error = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("library.Users_RegisterMember", new
        {
            actor.Id,
            UserName = data["Email"],
            NormalizedUserName = data["Email"].ToUpperInvariant(),
            Email = data["Email"],
            NormalizedEmail = data["Email"].ToUpperInvariant(),
            actor.PasswordHash,
            actor.SecurityStamp,
            actor.ConcurrencyStamp,
            FullName = data["FullName"]
        }, commandType: System.Data.CommandType.StoredProcedure));
        Assert.Contains(error.Number, new[] { 2601, 2627 });
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM library.Members WHERE Email=@Email", new { Email = data["Email"] }));
    }

    [SqlFact]
    public async Task Public_registration_is_rate_limited()
    {
        await using var app = Factory(); using var client = Client(app);
        var data = Input(); data["FullName"] = "";
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await Register(client, data)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Register(client, data)).StatusCode);
    }
}
