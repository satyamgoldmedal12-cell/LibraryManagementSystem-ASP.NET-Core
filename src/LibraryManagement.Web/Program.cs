using LibraryManagement.Infrastructure;
using LibraryManagement.Infrastructure.Logging;
using LibraryManagement.Web.Filters;
using LibraryManagement.Web.Middleware;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();
try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseLibraryLogging();
    builder.Services.AddControllersWithViews(options => { options.AddLibraryFilters(); options.Filters.Add<ModulePermissionFilter>(); })
        .AddCookieTempDataProvider(options =>
        {
            options.Cookie.Name = "LibraryManagement.TempData";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
        });
    builder.Services.AddScoped<ModulePermissionFilter>();
    builder.Services.AddAntiforgery(options =>
    {
        options.HeaderName = "X-CSRF-TOKEN";
        options.Cookie.Name = "LibraryManagement.Antiforgery";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
    });
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();
    builder.Services.AddLibraryInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());
    builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("report-export", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        options.AddPolicy("book-management", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        options.AddPolicy("registration", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 5, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
        options.AddPolicy("password-recovery", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 5, Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));
        options.AddPolicy("password-reset", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 10, Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));
    });
    var app = builder.Build();
    if (args.Contains("--bootstrap-admin", StringComparer.Ordinal))
    {
        await app.Services.BootstrapLibraryAdministratorAsync(builder.Configuration);
        Log.Information("Administrator created. Remove BootstrapAdmin configuration before running the web application.");
        return;
    }
    app.UseMiddleware<SecurityHeadersMiddleware>();
    // Wrap the handler so request logging sees the sanitized response rather than raw exceptions.
    app.UseLibraryRequestLogging();
    app.UseExceptionHandler();
    if (!app.Environment.IsDevelopment()) app.UseHsts();
    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllerRoute("module-path-alias", "ModulePath/{action=Index}/{id?}", new { controller="ModulePaths" });
    app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
    app.Run();
}
catch (Exception ex) { Log.Fatal(ex, "Application stopped unexpectedly"); throw; }
finally { Log.CloseAndFlush(); }

public partial class Program { }
