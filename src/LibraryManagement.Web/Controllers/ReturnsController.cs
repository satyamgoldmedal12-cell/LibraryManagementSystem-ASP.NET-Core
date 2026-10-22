using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagement.Web.Controllers;

[Authorize(Roles = Roles.Staff)]
public sealed class ReturnsController(ICirculationService circulation, ILibraryQueries queries) : CirculationControllerBase(circulation,queries)
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] CirculationFilter filter, CancellationToken ct) => View(await Ledger("returns",filter,ct));

    [HttpGet]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct)
    {
        var loan = await Circulation.DetailsAsync(id,ct);
        if (loan is null) return NotFound();
        return loan.ReturnedAt.HasValue ? RedirectToAction(nameof(Details),new { id }) : View(loan);
    }

    [HttpPost]
    [ActionName("Confirm")]
    public async Task<IActionResult> ConfirmReturn(int id, CancellationToken ct)
    {
        try
        {
            await Circulation.ReturnAsync(id,Actor,ct);
            Success("Book returned. Available quantity and final fine have been updated.");
            return RedirectToAction(nameof(Details),new { id });
        }
        catch (BusinessException ex) { ModelState.AddModelError("",ex.Message); }
        var loan = await Circulation.DetailsAsync(id,ct);
        return loan is null ? NotFound() : View("Confirm",loan);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var loan = await Circulation.DetailsAsync(id,ct);
        return loan?.ReturnedAt is null ? NotFound() : View(loan);
    }
}
