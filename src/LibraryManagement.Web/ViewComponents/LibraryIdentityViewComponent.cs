using LibraryManagement.Application.Interfaces;
using LibraryManagement.Application.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagement.Web.ViewComponents;

public sealed class LibraryIdentityViewComponent(ILibrarySettingsService settings, ILogger<LibraryIdentityViewComponent> logger) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        LibrarySettings model;
        try { model = await settings.GetAsync(HttpContext.RequestAborted); }
        catch (Exception ex) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            // Branding must still render on the error screen if the database is unavailable.
            logger.LogWarning("Library branding could not be loaded ({ExceptionType}).", ex.GetType().FullName);
            model = new LibrarySettings();
        }
        return View(model);
    }
}
