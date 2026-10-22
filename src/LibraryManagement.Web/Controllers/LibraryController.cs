using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagement.Web.Controllers;

[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public abstract class LibraryController : Controller
{
    protected string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Authenticated user ID is missing.");

    protected static int PageNumber(int page) => Math.Clamp(page, 1, 100000);

    protected static string? SearchTerm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed[..Math.Min(trimmed.Length, 200)];
    }

    protected void Success(string text) => TempData["Success"] = text;
}
