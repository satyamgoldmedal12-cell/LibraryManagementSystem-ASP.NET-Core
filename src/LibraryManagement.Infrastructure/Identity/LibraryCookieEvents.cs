using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LibraryManagement.Infrastructure.Identity;

public sealed class LibraryCookieEvents(IUserSessionRepository sessions, IOptions<AuthenticationSessionOptions> options, TimeProvider clock)
    : CookieAuthenticationEvents
{
    public const string SessionKey = "Library.SessionId";

    public override async Task SigningIn(CookieSigningInContext context)
    {
        var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("A signed-in user must have an ID.");
        if (context.Properties.Items.TryGetValue(SessionKey, out var previous) && Guid.TryParse(previous, out var previousId))
            await sessions.RevokeAsync(previousId, userId, context.HttpContext.RequestAborted);
        var id = Guid.NewGuid();
        var device = context.HttpContext.Request.Headers.UserAgent.ToString();
        device = string.IsNullOrWhiteSpace(device) ? "Browser session" : device[..Math.Min(device.Length, 160)];
        await sessions.CreateAsync(id, userId, device, options.Value.AbsoluteHours, context.HttpContext.RequestAborted);
        context.Properties.Items[SessionKey] = id.ToString();
        context.Properties.IssuedUtc = clock.GetUtcNow();
        context.Properties.ExpiresUtc = clock.GetUtcNow().AddMinutes(options.Value.IdleMinutes);
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        // Retain Identity's security stamp validation; a password or access change invalidates old cookies.
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if (context.Principal is null) return;
        var userId = context.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is not null && context.Properties.Items.TryGetValue(SessionKey, out var value)
            && Guid.TryParse(value, out var id)
            && await sessions.ValidateAsync(id, userId, options.Value.IdleMinutes, context.HttpContext.RequestAborted)) return;
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }

    public override async Task SigningOut(CookieSigningOutContext context)
    {
        // Read the middleware's already-completed authentication result; do not recursively authenticate.
        var result = context.HttpContext.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult;
        var userId = result?.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is not null && result?.Properties?.Items.TryGetValue(SessionKey, out var value) == true
            && Guid.TryParse(value, out var id))
            await sessions.RevokeAsync(id, userId, context.HttpContext.RequestAborted);
    }
}
