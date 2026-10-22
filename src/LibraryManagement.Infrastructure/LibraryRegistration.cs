using LibraryManagement.Application.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace LibraryManagement.Infrastructure;

// The host calls this only from its composition root. No controller knows implementation types.
public static class LibraryRegistration
{
    public static IServiceCollection AddLibraryInfrastructure(this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        services.AddSingleton<SqlConnectionFactory>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ILibraryRepository, LibraryRepository>();
        services.AddScoped<IModuleAccessRepository, ModuleAccessRepository>();
        services.AddScoped<IModuleAccessService, ModuleAccessService>();
        services.AddScoped<ILibraryQueries, LibraryQueries>();
        services.AddScoped<ILibraryService, LibraryService>();
        services.AddScoped<IBookIssueService, BookIssueService>();
        services.AddScoped<ILibrarySettingsRepository, LibrarySettingsRepository>();
        services.AddScoped<ILibrarySettingsService, LibrarySettingsService>();
        services.AddScoped<ICirculationRepository, CirculationRepository>();
        services.AddScoped<ICirculationService, CirculationService>();
        services.AddScoped<IReportRepository, ReportRepository>();
        services.AddScoped<IReportService, ReportService>();
        services.AddSingleton<IReportExporter, LibraryManagement.Infrastructure.Reporting.ReportExporter>();
        services.AddScoped<IBookManagementService, BookManagementService>();
        services.AddScoped<IMemberManagementService, MemberManagementService>();
        services.AddSingleton<IBookCoverProcessor, LibraryManagement.Infrastructure.Images.BookCoverProcessor>();
        services.AddScoped<IMemberRegistrationService, MemberRegistrationService>();
        services.AddScoped<IMemberAccountProvisioner, MemberAccountProvisioner>();
        services.AddScoped<IUserSessionRepository, UserSessionRepository>();
        services.AddScoped<IUserSessionService, UserSessionService>();
        services.AddScoped<IAccountService, IdentityAccountService>();
        services.AddScoped<IUserAccountIdentity, UserAccountIdentity>();
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        services.AddScoped<LibraryCookieEvents>();
        services.AddScoped<IPasswordRecoveryService, PasswordRecoveryService>();
        services.AddScoped<IPasswordResetEmailSender, PasswordResetEmailSender>();
        services.AddHttpContextAccessor();
        services.AddOptions<AuthenticationSessionOptions>().BindConfiguration("AuthenticationSessions").ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<PasswordRecoveryOptions>().BindConfiguration("PasswordRecovery").ValidateDataAnnotations()
            .Validate(o => Uri.TryCreate(o.PublicOrigin, UriKind.Absolute, out var uri) && uri.Scheme == "https"
                && string.IsNullOrEmpty(uri.UserInfo) && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment),
                "PasswordRecovery:PublicOrigin must be an HTTPS origin without a path, credentials, query, or fragment.").ValidateOnStart();
        services.AddOptions<RecoveryEmailOptions>().BindConfiguration("RecoveryEmail").ValidateDataAnnotations()
            .Validate(o => o.DeliveryMethod != "Pickup" || isDevelopment, "Email pickup is Development-only.")
            .Validate(o => o.DeliveryMethod != "Smtp" || (!string.IsNullOrWhiteSpace(o.Host)
                && (string.IsNullOrWhiteSpace(o.UserName) == string.IsNullOrWhiteSpace(o.Password))),
                "Configure the SMTP host and matching username/password settings.").ValidateOnStart();
        services.AddOptions<LendingOptions>().BindConfiguration("Lending").ValidateDataAnnotations().ValidateOnStart();
        services.AddScoped(sp => sp.GetRequiredService<IOptions<LendingOptions>>().Value);
        services.AddScoped(sp => sp.GetRequiredService<IOptions<AuthenticationSessionOptions>>().Value);
        services.Configure<PasswordHasherOptions>(o => o.IterationCount = 210_000);
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromMinutes(30));
        services.AddDataProtection().SetApplicationName("LibraryManagement");
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddUserStore<DapperUserStore>().AddSignInManager().AddDefaultTokenProviders();
        services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, LibraryClaimsFactory>();
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
            options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
        }).AddIdentityCookies();
        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.Cookie.Name = "LibraryManagement.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(configuration.GetValue("AuthenticationSessions:IdleMinutes", 30));
            options.EventsType = typeof(LibraryCookieEvents);
            options.SlidingExpiration = true;
        });
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
        return services;
    }

    public static async Task BootstrapLibraryAdministratorAsync(this IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.CreateScope();
        var email = configuration["BootstrapAdmin:Email"] ?? throw new InvalidOperationException("Set BootstrapAdmin__Email.");
        var password = configuration["BootstrapAdmin:Password"] ?? throw new InvalidOperationException("Set BootstrapAdmin__Password.");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(email) is not null) throw new InvalidOperationException("Account already exists. Bootstrap does not change existing accounts.");
        var result = await users.CreateAsync(new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = "Library Administrator",
            Role = Roles.Admin,
            LockoutEnabled = true
        }, password);
        if (!result.Succeeded) throw new InvalidOperationException(string.Join(" ", result.Errors.Select(x => x.Description)));
    }
}
