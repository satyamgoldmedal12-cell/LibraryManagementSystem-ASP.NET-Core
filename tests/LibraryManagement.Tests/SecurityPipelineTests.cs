using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace LibraryManagement.Tests;

[Collection("MVC host")]
public sealed class SecurityPipelineTests
{
    private const string Secret = "PrivatePasswordAndResetToken_7f894c";
    private sealed class Sink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }
    private static (WebApplicationFactory<Program> Host, Mock<IAccountService> Accounts, Sink Logs) Host(
        Exception? fault = null, string environment = "Development", bool brandingFailure = false)
    {
        var accounts = new Mock<IAccountService>();
        if (fault is null) accounts.Setup(x => x.LoginAsync(It.IsAny<LoginModel>())).ReturnsAsync(OperationResult.Failed("SignInFailed", "Sign-in failed."));
        else accounts.Setup(x => x.LoginAsync(It.IsAny<LoginModel>())).ThrowsAsync(fault);
        var settings = new Mock<ILibrarySettingsService>();
        if (brandingFailure) settings.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException(Secret));
        else settings.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new LibrarySettings());
        var sink = new Sink();
        var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment(environment)
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "*", ["PasswordRecovery:PublicOrigin"] = "https://library.example.test",
                ["RecoveryEmail:DeliveryMethod"] = "Smtp", ["RecoveryEmail:Host"] = "smtp.example.test"
            })).ConfigureServices(services =>
            {
                services.RemoveAll<IAccountService>(); services.AddSingleton(accounts.Object);
                services.RemoveAll<ILibrarySettingsService>(); services.AddSingleton(settings.Object);
                services.AddSingleton<ILogEventSink>(sink);
                services.Configure<HttpsRedirectionOptions>(o => o.HttpsPort = 443);
            }));
        return (host, accounts, sink);
    }
    private static HttpClient Client(WebApplicationFactory<Program> host, string scheme = "https") => host.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new Uri(scheme + "://library.example.test"), AllowAutoRedirect = false });
    private static string Token(string html) => Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    private static Dictionary<string, string> Fields(string? token = null) => new()
    { ["Email"] = "reader@example.test", ["Password"] = Secret, ["__RequestVerificationToken"] = token ?? "" };
    private static void AssertNoSecrets(Sink logs)
    {
        Assert.NotEmpty(logs.Events);
        Assert.All(logs.Events, e => Assert.DoesNotContain(Secret, e.RenderMessage() + e.Exception));
    }

    [Theory]
    [InlineData("Development", "text/html")]
    [InlineData("Development", "application/json")]
    [InlineData("Development", "invalid accept value")]
    [InlineData("Production", "text/html")]
    [InlineData("Production", "application/problem+json")]
    public async Task Unhandled_errors_are_safe_and_logged_without_submitted_secrets(string environment, string accept)
    {
        var (factory, _, logs) = Host(new InvalidOperationException("SQL or password detail: " + Secret), environment);
        await using var host = factory; using var client = Client(host);
        var token = Token(await client.GetStringAsync("/Account/Login"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login?code=" + Secret)
        { Content = new FormUrlEncodedContent(Fields(token)) };
        request.Headers.TryAddWithoutValidation("Accept", accept);
        var result = await client.SendAsync(request); var body = await result.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.Contains("no-store", result.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", Assert.Single(result.Headers.GetValues("X-Content-Type-Options")));
        Assert.Contains("frame-ancestors 'none'", Assert.Single(result.Headers.GetValues("Content-Security-Policy")));
        Assert.DoesNotContain(Secret, body); Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("StackTrace", body); Assert.DoesNotContain("SQL or password detail", body);
        if (accept.Contains("json", StringComparison.Ordinal))
        {
            Assert.Equal("application/problem+json", result.Content.Headers.ContentType!.MediaType);
            using var json = JsonDocument.Parse(body); Assert.Equal(500, json.RootElement.GetProperty("status").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("reference").GetString()));
        }
        else { Assert.Contains("Reference:", body); Assert.Contains("Back to library", body); }
        Assert.Contains(logs.Events, e => e.Properties.ContainsKey("ErrorReference") && e.Properties.ContainsKey("ExceptionType"));
        Assert.Contains(logs.Events, e => e.Properties.ContainsKey("RequestPath") && e.Exception is null);
        AssertNoSecrets(logs);
    }

    [Fact]
    public async Task Oversized_request_failures_have_safe_413_responses()
    {
        var (factory, _, logs) = Host(new BadHttpRequestException(Secret, StatusCodes.Status413PayloadTooLarge));
        await using var host = factory; using var client = Client(host);
        var token = Token(await client.GetStringAsync("/Account/Login"));
        var result = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(Fields(token)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, result.StatusCode);
        Assert.Contains("request is too large", await result.Content.ReadAsStringAsync()); AssertNoSecrets(logs);
    }

    [Fact]
    public async Task Branding_failure_keeps_the_login_usable_and_logs_only_exception_type()
    {
        var (factory, _, logs) = Host(brandingFailure: true); await using var host = factory; using var client = Client(host);
        var response = await client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Contains("Library Desk", await response.Content.ReadAsStringAsync());
        Assert.Contains(logs.Events, e => e.Level == LogEventLevel.Warning); AssertNoSecrets(logs);
    }

    [Fact]
    public async Task Antiforgery_and_tempdata_cookies_are_secure_and_tokens_are_bound_to_the_cookie()
    {
        var (factory, accounts, _) = Host(); await using var host = factory;
        using var first = Client(host); using var second = Client(host);
        var response = await first.GetAsync("/Account/Login"); var token = Token(await response.Content.ReadAsStringAsync());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), x => x.StartsWith("LibraryManagement.Antiforgery=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie.ToLowerInvariant()); Assert.Contains("httponly", cookie.ToLowerInvariant()); Assert.Contains("samesite=strict", cookie.ToLowerInvariant());
        var otherToken = Token(await second.GetStringAsync("/Account/Login"));
        Assert.Equal(HttpStatusCode.BadRequest, (await second.PostAsync("/Account/Login", new FormUrlEncodedContent(Fields(token)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsync("/Account/Login", new FormUrlEncodedContent(Fields(otherToken)))).StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login") { Content = new FormUrlEncodedContent(Fields()) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        Assert.Equal(HttpStatusCode.OK, (await first.SendAsync(request)).StatusCode);
        accounts.Verify(x => x.LoginAsync(It.IsAny<LoginModel>()), Times.Once);
        var options = host.Services.GetRequiredService<IOptions<CookieTempDataProviderOptions>>().Value;
        Assert.True(options.Cookie.HttpOnly); Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy); Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
    }

    [Fact]
    public async Task Invalid_authentication_input_is_rejected_before_the_account_service_runs()
    {
        var (factory, accounts, _) = Host(); await using var host = factory; using var client = Client(host);
        var token = Token(await client.GetStringAsync("/Account/Login"));
        foreach (var data in new[] { new Dictionary<string,string> { ["Email"] = "invalid", ["Password"] = Secret },
            new Dictionary<string,string> { ["Email"] = "reader@example.test", ["Password"] = new string('X',129) },
            new Dictionary<string,string> { ["Email"] = "reader@example.test", ["Password"] = Secret, ["Portal"] = "SuperAdmin" } })
        {
            data["__RequestVerificationToken"] = token;
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/Account/Login", new FormUrlEncodedContent(data))).StatusCode);
        }
        accounts.Verify(x => x.LoginAsync(It.IsAny<LoginModel>()), Times.Never);
    }

    [Fact]
    public async Task Production_requires_https_and_sets_hsts_on_non_loopback_hosts()
    {
        var (factory, _, _) = Host(environment: "Production"); await using var host = factory;
        using var http = Client(host, "http"); var redirect = await http.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.TemporaryRedirect, redirect.StatusCode); Assert.Equal("https", redirect.Headers.Location!.Scheme);
        using var https = Client(host); var page = await https.GetAsync("/Account/Login");
        Assert.Contains("max-age=", Assert.Single(page.Headers.GetValues("Strict-Transport-Security")));
        Assert.Contains("no-store", page.Headers.CacheControl!.ToString());
    }
}
