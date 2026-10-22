using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagement.Web.Controllers;

[Authorize(Roles = Roles.Staff)]
public sealed class FinesController(ICirculationService circulation, ILibraryQueries queries) : CirculationControllerBase(circulation,queries)
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] CirculationFilter filter, CancellationToken ct) => View(await Ledger("fines",filter,ct));

    [HttpGet]
    public async Task<IActionResult> Collection([FromQuery] CirculationFilter filter, CancellationToken ct) => View(await Ledger("collections",filter,ct));

    [HttpGet]
    public async Task<IActionResult> Pay(int id, CancellationToken ct)
    {
        var loan = await Circulation.DetailsAsync(id,ct);
        if (loan is null) return NotFound();
        if (loan.PaymentId.HasValue) return RedirectToAction(nameof(Receipt),new { id });
        if (loan.ReturnedAt is null || loan.FineAmount <= 0) return BadRequest("Return the book and finalize a positive fine before recording payment.");
        return View(loan);
    }

    [HttpPost, ActionName("Pay")]
    public async Task<IActionResult> RecordPayment(int id, CancellationToken ct)
    {
        try
        {
            await Circulation.PayFineAsync(id,Actor,ct);
            Success("Full fine payment recorded.");
            return RedirectToAction(nameof(Receipt),new { id });
        }
        catch (BusinessException ex) { ModelState.AddModelError("",ex.Message); }
        var loan = await Circulation.DetailsAsync(id,ct);
        return loan is null ? NotFound() : View("Pay",loan);
    }

    [HttpGet]
    public async Task<IActionResult> Receipt(int id, CancellationToken ct)
    {
        var loan = await Circulation.DetailsAsync(id,ct);
        return loan?.PaymentId is null ? NotFound() : View(loan);
    }

    [HttpGet, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Settings(CancellationToken ct) => View(await Circulation.PolicyAsync(ct));

    [HttpPost, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Settings([Bind("DailyFine,Revision")] FinePolicy policy, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await Circulation.SavePolicyAsync(policy,Actor,ct);
                Success("Fine per day saved. The new rate applies to future checkouts.");
                return RedirectToAction(nameof(Settings));
            }
            catch (BusinessException ex) { ModelState.AddModelError("",ex.Message); }
        }
        return View(policy);
    }
}
