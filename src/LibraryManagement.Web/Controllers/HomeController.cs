using LibraryManagement.Web.ViewModels;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagement.Web.Controllers;

public sealed class HomeController(ILibraryQueries queries, IModuleAccessService permissions) : LibraryController
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!await permissions.AllowsAsync(Actor,"Home",ModuleRight.Read,ct))
        {
            var menu=await permissions.MenuAsync(Actor,ct);
            var first=User.IsInRole(Roles.Member)?menu.FirstOrDefault(x=>x.ControllerName=="Loans")??menu.FirstOrDefault():menu.FirstOrDefault();
            return first is null?View("NoModules"):RedirectToAction(first.ActionName,first.ControllerName);
        }

        return View(await queries.DashboardAsync(ct));
    }

    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Audit(CancellationToken ct) => View(await queries.AuditAsync(ct));

    [AllowAnonymous, IgnoreAntiforgeryToken]
    public IActionResult Error()
    {
        Response.StatusCode = 500;
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
