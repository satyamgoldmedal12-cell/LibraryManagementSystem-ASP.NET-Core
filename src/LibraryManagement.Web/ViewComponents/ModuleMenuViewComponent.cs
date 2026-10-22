using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
namespace LibraryManagement.Web.ViewComponents;
public sealed class ModuleMenuViewComponent(IModuleAccessService permissions) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var userId=HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return View(new LibraryManagement.Web.ViewModels.ModuleNavigationViewModel(userId is null?Array.Empty<ModuleMenuItem>():await permissions.MenuAsync(userId,HttpContext.RequestAborted),await permissions.ModulesAsync(HttpContext.RequestAborted)));
    }
}
