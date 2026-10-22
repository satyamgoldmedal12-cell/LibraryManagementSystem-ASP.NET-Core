using System.Diagnostics;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagement.Web.Middleware;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var status = exception is BadHttpRequestException { StatusCode: 400 or 413 } badRequest
            ? badRequest.StatusCode : StatusCodes.Status500InternalServerError;
        var reference = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        // Exception messages can contain SQL, connection details or submitted values. Keep diagnostics structural.
        var applicationFrame = new StackTrace(exception).GetFrames().Select(f => f.GetMethod())
            .FirstOrDefault(m => m?.DeclaringType?.Assembly.GetName().Name?.StartsWith("LibraryManagement.", StringComparison.Ordinal) == true);
        var location = applicationFrame is null ? "Unknown" : applicationFrame.DeclaringType?.FullName + "." + applicationFrame.Name;
        logger.Log(status >= 500 ? LogLevel.Error : LogLevel.Warning,
            "Request failed with status {StatusCode}. Reference {ErrorReference}; type {ExceptionType}; endpoint {Endpoint}; application location {ApplicationLocation}",
            status, reference, exception.GetType().FullName, context.GetEndpoint()?.DisplayName ?? "Unmatched", location);

        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        var title = status == 500 ? "We couldn't complete that request." : "Check your request.";
        var detail = status == 413 ? "The request is too large. Upload a cover up to 2 MB."
            : status == 400 ? "Check the submitted information and try again."
            : "Please try again. If the problem continues, contact your administrator with the reference below.";
        var wantsJson = context.Request.Headers.Accept.ToString().Split(',').Any(value =>
            value.Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || value.Split(';')[0].Trim().Equals("application/problem+json", StringComparison.OrdinalIgnoreCase))
            || string.Equals(context.Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        if (wantsJson)
        {
            var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
            problem.Extensions["reference"] = reference;
            await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: ct);
        }
        else
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            var encodedReference = HtmlEncoder.Default.Encode(reference);
            var css = HtmlEncoder.Default.Encode(context.Request.PathBase.Add("/css/site.css").Value!);
            var home = HtmlEncoder.Default.Encode(context.Request.PathBase.Add("/").Value!);
            // A standalone response also works when authentication, SQL or Razor rendering itself failed.
            await context.Response.WriteAsync($"""
                <!DOCTYPE html>
                <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
                <title>Library Desk · Request error</title><link rel="stylesheet" href="{css}"></head>
                <body><main class="empty-state" role="main"><p class="eyebrow">PLEASE TRY AGAIN</p>
                <h1>{HtmlEncoder.Default.Encode(title)}</h1><p>{HtmlEncoder.Default.Encode(detail)}</p>
                <p>Reference: <strong>{encodedReference}</strong></p><a href="{home}">Back to library</a></main></body></html>
                """, ct);
        }
        return true;
    }
}
