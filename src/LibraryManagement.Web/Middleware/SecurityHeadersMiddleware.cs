using Microsoft.AspNetCore.Mvc.Controllers;

namespace LibraryManagement.Web.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        // Apply just before headers are sent: exception handlers may clear an earlier response.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            var accountPage = context.Request.Path.StartsWithSegments("/Account");
            context.Response.Headers["Referrer-Policy"] = accountPage ? "no-referrer" : "strict-origin-when-cross-origin";
            if (accountPage || context.User.Identity?.IsAuthenticated == true
                || context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>() is not null
                || context.Response.StatusCode >= 400)
                context.Response.Headers["Cache-Control"] = "no-store";
            if (accountPage) context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
            return Task.CompletedTask;
        });
        return next(context);
    }
}
