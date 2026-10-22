using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LibraryManagement.Tests;

[Collection("MVC host")]
public sealed class AuthenticationIntegrationTests
{
    private sealed class EmailCapture : IPasswordResetEmailSender
    {
        public ConcurrentQueue<(string Recipient, string Link)> Messages { get; } = new();
        public bool Fail { get; set; }
        public Task SendAsync(string recipient, string link, CancellationToken ct)
        {
            if (Fail) throw new InvalidOperationException("Private SMTP details must not reach the response.");
            Messages.Enqueue((recipient, link)); return Task.CompletedTask;
        }
    }
    private static (WebApplicationFactory<Program> App, EmailCapture Email) Host(bool expiredTokens = false)
    {
        var connection = Environment.GetEnvironmentVariable("LIBRARY_TEST_CONNECTION")!;
        if (!new SqlConnectionStringBuilder(connection).InitialCatalog.EndsWith("_Test", StringComparison.Ordinal))
            throw new InvalidOperationException("Authentication tests require the dedicated _Test database.");
        var email = new EmailCapture();
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:LibraryDatabase"] = connection,
                    ["PasswordRecovery:PublicOrigin"] = "https://library.example.test",
                    ["AllowedHosts"] = "*"
                })).ConfigureServices(services =>
                {
                    services.RemoveAll<IPasswordResetEmailSender>();
                    services.AddSingleton<IPasswordResetEmailSender>(email);
                    if (expiredTokens) services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromSeconds(-1));
                }));
        return (app, email);
    }
    private static HttpClient Client(WebApplicationFactory<Program> app) => app.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static string Token(string html) => Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    private static string Password() => "Test!" + Guid.NewGuid().ToString("N") + "7aA";
    private static async Task<(ApplicationUser User, string Password)> UserAsync(WebApplicationFactory<Program> app, string role = Roles.Librarian)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = Guid.NewGuid() + "@auth.test"; var password = Password();
        if (role == Roles.Member)
        {
            var result = await scope.ServiceProvider.GetRequiredService<IMemberRegistrationService>().RegisterAsync(
                new RegisterModel { Email = email, FullName = "Security Test Reader", Password = password, ConfirmPassword = password });
            Assert.True(result.Succeeded);
            return ((await users.FindByEmailAsync(email))!, password);
        }
        var user = new ApplicationUser { FullName = "Security Test " + role, Email = email, UserName = email, Role = role };
        Assert.True((await users.CreateAsync(user, password)).Succeeded); return (user, password);
    }
    private static async Task<HttpResponseMessage> Post(HttpClient client, string action, Dictionary<string, string> values, string? formUrl = null)
    {
        values["__RequestVerificationToken"] = Token(await client.GetStringAsync(formUrl ?? action));
        return await client.PostAsync(action, new FormUrlEncodedContent(values));
    }
    private static async Task<HttpResponseMessage> Login(HttpClient client, ApplicationUser user, string password, string? portal = null)
    {
        var values = new Dictionary<string, string> { ["Email"] = user.Email!, ["Password"] = password };
        if (portal is not null) values["Portal"] = portal;
        return await Post(client, "/Account/Login", values);
    }
    private static string Cookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("LibraryManagement.Auth=", StringComparison.Ordinal)).Split(';')[0];
    private static HttpClient Replay(WebApplicationFactory<Program> app, string cookie)
    {
        var client = Client(app); client.DefaultRequestHeaders.Add("Cookie", cookie); return client;
    }
    private static async Task AssertSignedOut(HttpClient client)
    {
        var page = await client.GetAsync("/Books");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Contains("/Account/Login", page.Headers.Location!.OriginalString);
    }
    private static Dictionary<string, string> ResetValues(string link, string password)
    {
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(link).Query);
        return new()
        {
            ["UserId"] = query["userId"].ToString(),
            ["Code"] = query["code"].ToString(),
            ["NewPassword"] = password,
            ["ConfirmPassword"] = password
        };
    }

    [SqlFact]
    public async Task Three_login_portals_use_real_roles_and_server_authorization()
    {
        var (host, _) = Host(); await using var app = host;
        foreach (var role in new[] { Roles.Admin, Roles.Librarian, Roles.Member })
        {
            var (user, password) = await UserAsync(app, role); using var client = Client(app);
            Assert.Contains(role + " sign in", await client.GetStringAsync("/Account/" + role + "Login"));
            Assert.Equal(HttpStatusCode.Redirect, (await Login(client, user, password, role)).StatusCode);
            Assert.Equal(role == Roles.Member ? HttpStatusCode.Redirect : HttpStatusCode.OK, (await client.GetAsync("/Members")).StatusCode);
            Assert.Equal(role == Roles.Admin ? HttpStatusCode.OK : HttpStatusCode.Redirect, (await client.GetAsync("/Users")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Loans")).StatusCode);
        }
    }
    [SqlFact]
    public async Task Selecting_an_admin_portal_does_not_elevate_a_member()
    {
        var (host, _) = Host(); await using var app = host;
        var (user, password) = await UserAsync(app, Roles.Member); using var client = Client(app);
        Assert.Equal(HttpStatusCode.OK, (await Login(client, user, password, Roles.Admin)).StatusCode);
        await AssertSignedOut(client);
        Assert.Equal(HttpStatusCode.Redirect, (await Login(client, user, password, Roles.Member)).StatusCode);
    }
    [SqlFact]
    public async Task Logout_is_post_only_csrf_protected_and_revokes_replayed_cookie()
    {
        var (host, _) = Host(); await using var app = host;
        var (user, password) = await UserAsync(app); using var client = Client(app);
        var response = await Login(client, user, password); var cookie = Cookie(response);
        var header = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("LibraryManagement.Auth="));
        Assert.Contains("secure", header.ToLowerInvariant()); Assert.Contains("httponly", header.ToLowerInvariant());
        Assert.Contains("samesite=lax", header.ToLowerInvariant());
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync("/Account/Logout")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Account/Logout", new(), "/Account/Sessions")).StatusCode);
        using var replay = Replay(app, cookie); await AssertSignedOut(replay);
    }
    [SqlFact]
    public async Task Session_revocation_is_owned_by_user_and_can_preserve_current_session()
    {
        var (host, _) = Host(); await using var app = host;
        var (user, password) = await UserAsync(app);
        using var first = Client(app); using var second = Client(app);
        await Login(first, user, password); await Login(second, user, password);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(first, "/Account/RevokeOtherSessions", new(), "/Account/Sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/Books")).StatusCode); await AssertSignedOut(second);
        var (other, otherPassword) = await UserAsync(app); using var outsider = Client(app); await Login(outsider, other, otherPassword);
        using var scope = app.Services.CreateScope();
        var otherSession = Assert.Single(await scope.ServiceProvider.GetRequiredService<IUserSessionRepository>().ListAsync(other.Id, 30, default));
        await Post(first, "/Account/RevokeSession", new() { ["id"] = otherSession.Id.ToString() }, "/Account/Sessions");
        Assert.Equal(HttpStatusCode.OK, (await outsider.GetAsync("/Books")).StatusCode);
    }
    [SqlFact]
    public async Task Idle_and_absolute_session_expiry_are_enforced_on_server()
    {
        var (host, _) = Host(); await using var app = host;
        var (user, password) = await UserAsync(app);
        using var scope = app.Services.CreateScope(); await using var connection = await scope.ServiceProvider.GetRequiredService<SqlConnectionFactory>().OpenAsync(default);
        using var idleClient = Client(app); await Login(idleClient, user, password);
        await connection.ExecuteAsync("UPDATE library.UserSessions SET LastSeenAt=DATEADD(minute,-31,SYSUTCDATETIME()) WHERE UserId=@Id", new { user.Id });
        await AssertSignedOut(idleClient);
        using var expiredClient = Client(app); await Login(expiredClient, user, password);
        await connection.ExecuteAsync("UPDATE library.UserSessions SET ExpiresAt=DATEADD(second,-1,SYSUTCDATETIME()) WHERE UserId=@Id", new { user.Id });
        await AssertSignedOut(expiredClient);
    }
    [SqlFact]
    public async Task Forgot_password_response_is_generic_and_link_uses_configured_origin()
    {
        var (host, email) = Host(); await using var app = host;
        var (user, _) = await UserAsync(app);
        using var client = Client(app); client.DefaultRequestHeaders.Host = "untrusted.example";
        var existing = await Post(client, "/Account/ForgotPassword", new() { ["Email"] = user.Email! });
        var unknown = await Post(client, "/Account/ForgotPassword", new() { ["Email"] = Guid.NewGuid() + "@missing.test" });
        Assert.Equal(HttpStatusCode.Redirect, existing.StatusCode); Assert.Equal(existing.StatusCode, unknown.StatusCode);
        Assert.Equal(existing.Headers.Location, unknown.Headers.Location);
        var message = Assert.Single(email.Messages);
        Assert.StartsWith("https://library.example.test/Account/ResetPassword?", message.Link);
        var page = await client.GetStringAsync("/Account/ForgotPasswordConfirmation");
        Assert.DoesNotContain(message.Link, page); Assert.DoesNotContain(user.Email!, page);
        Assert.DoesNotContain("Code=", page);
    }
    [SqlFact]
    public async Task Reset_token_is_single_use_and_invalidates_all_old_sessions()
    {
        var (host, email) = Host(); await using var app = host;
        var (user, oldPassword) = await UserAsync(app); using var signedIn = Client(app);
        var cookie = Cookie(await Login(signedIn, user, oldPassword));
        using var anonymous = Client(app);
        await Post(anonymous, "/Account/ForgotPassword", new() { ["Email"] = user.Email! });
        var link = Assert.Single(email.Messages).Link; var newPassword = Password();
        var values = ResetValues(link, newPassword);
        var formUrl = new Uri(link).PathAndQuery;
        Assert.Equal(HttpStatusCode.Redirect, (await Post(anonymous, "/Account/ResetPassword", values, formUrl)).StatusCode);
        using var scope = app.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var updated = (await users.FindByIdAsync(user.Id))!;
        Assert.False(await users.CheckPasswordAsync(updated, oldPassword)); Assert.True(await users.CheckPasswordAsync(updated, newPassword));
        using var replay = Replay(app, cookie); await AssertSignedOut(replay);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IUserSessionRepository>().ListAsync(user.Id, 30, default));
        var reused = await Post(anonymous, "/Account/ResetPassword", values, formUrl);
        Assert.Equal(HttpStatusCode.OK, reused.StatusCode);
        Assert.Contains("invalid or expired", await reused.Content.ReadAsStringAsync());
    }
    [SqlFact]
    public async Task Tampered_and_expired_reset_links_do_not_change_password()
    {
        var (host, email) = Host(expiredTokens: true); await using var app = host;
        var (user, oldPassword) = await UserAsync(app); using var client = Client(app);
        await Post(client, "/Account/ForgotPassword", new() { ["Email"] = user.Email! });
        var link = Assert.Single(email.Messages).Link; var values = ResetValues(link, Password()); var formUrl = new Uri(link).PathAndQuery;
        var expired = await Post(client, "/Account/ResetPassword", values, formUrl);
        Assert.Equal(HttpStatusCode.OK, expired.StatusCode); Assert.Contains("invalid or expired", await expired.Content.ReadAsStringAsync());
        values["Code"] = "%%%invalid%%%";
        var invalid = await Post(client, "/Account/ResetPassword", values, formUrl);
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        using var scope = app.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await users.CheckPasswordAsync((await users.FindByIdAsync(user.Id))!, oldPassword));
    }
    [SqlFact]
    public async Task Forgot_and_reset_posts_require_csrf_and_recovery_is_throttled()
    {
        var (host, _) = Host(); await using var app = host; using var client = Client(app);
        var missing = await client.PostAsync("/Account/ForgotPassword", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = "reader@example.test" }));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var reset = await client.PostAsync("/Account/ResetPassword", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        // The first rejected POST also consumes one of the five permitted requests.
        for (var i = 0; i < 4; i++) Assert.Equal(HttpStatusCode.Redirect,
            (await Post(client, "/Account/ForgotPassword", new() { ["Email"] = "missing@example.test" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Post(client, "/Account/ForgotPassword", new() { ["Email"] = "missing@example.test" })).StatusCode);
    }
    [SqlFact]
    public async Task Change_password_requires_current_password_and_rotates_sessions()
    {
        var (host, _) = Host(); await using var app = host; var (user, oldPassword) = await UserAsync(app);
        using var first = Client(app); using var second = Client(app);
        var oldCookie = Cookie(await Login(first, user, oldPassword)); await Login(second, user, oldPassword);
        var newPassword = Password();
        var fields = new Dictionary<string, string> { ["CurrentPassword"] = "Wrong!Password123", ["NewPassword"] = newPassword, ["ConfirmPassword"] = newPassword };
        Assert.Equal(HttpStatusCode.OK, (await Post(first, "/Account/ChangePassword", fields)).StatusCode);
        fields["CurrentPassword"] = oldPassword;
        Assert.Equal(HttpStatusCode.Redirect, (await Post(first, "/Account/ChangePassword", fields)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/Books")).StatusCode);
        await AssertSignedOut(second); using var replay = Replay(app, oldCookie); await AssertSignedOut(replay);
    }
    [SqlFact]
    public async Task Five_failed_passwords_lock_account_even_when_next_password_is_correct()
    {
        var (host, _) = Host(); await using var app = host; var (user, password) = await UserAsync(app); using var client = Client(app);
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await Login(client, user, "Wrong!Password123")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(client, user, password)).StatusCode);
        await AssertSignedOut(client);
        using var scope = app.Services.CreateScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(user.Id))!.LockoutEnd > DateTimeOffset.UtcNow);
    }
    [SqlFact]
    public async Task Admin_disable_ends_sessions_and_does_not_allow_password_recovery()
    {
        var (host, email) = Host(); await using var app = host;
        var (admin, adminPassword) = await UserAsync(app, Roles.Admin); var (user, password) = await UserAsync(app);
        using var administrator = Client(app); using var target = Client(app);
        await Login(administrator, admin, adminPassword); await Login(target, user, password);
        await Post(administrator, "/Users/SetAccess", new() { ["id"] = user.Id, ["enabled"] = "false" }, "/Users");
        await AssertSignedOut(target); using var anonymous = Client(app);
        await Post(anonymous, "/Account/ForgotPassword", new() { ["Email"] = user.Email! });
        Assert.Empty(email.Messages);
        Assert.Equal(HttpStatusCode.OK, (await Login(anonymous, user, password)).StatusCode);
    }
    [SqlFact]
    public async Task Passwords_have_independent_salts_and_configured_work_factor()
    {
        var (host, _) = Host(); await using var app = host; using var scope = app.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>(); var password = Password();
        var first = hasher.HashPassword(new ApplicationUser(), password); var second = hasher.HashPassword(new ApplicationUser(), password);
        Assert.NotEqual(first, second); Assert.NotEqual(password, first);
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(new ApplicationUser(), first, password));
        var bytes = Convert.FromBase64String(first);
        Assert.Equal(1, bytes[0]); Assert.Equal(210_000u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5, 4)));
        Assert.Equal(210_000, scope.ServiceProvider.GetRequiredService<IOptions<PasswordHasherOptions>>().Value.IterationCount);
    }
    [SqlFact]
    public async Task Delivery_failure_does_not_expose_private_details_or_account_existence()
    {
        var (host, email) = Host(); await using var app = host; email.Fail = true;
        var (user, _) = await UserAsync(app); using var client = Client(app);
        var response = await Post(client, "/Account/ForgotPassword", new() { ["Email"] = user.Email! });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("Private SMTP details", await client.GetStringAsync(response.Headers.Location!));
    }
}
