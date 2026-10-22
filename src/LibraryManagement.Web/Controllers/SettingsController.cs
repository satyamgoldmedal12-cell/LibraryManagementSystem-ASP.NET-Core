using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagement.Web.Controllers;

[Authorize(Roles = Roles.Admin)]
public sealed class SettingsController(ILibrarySettingsService settings) : LibraryController
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await settings.GetAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Index(
        [Bind("LibraryName,Address,Email,Phone,MaxActiveLoans,DefaultIssueDays,DailyFine,MembershipDurationMonths,Revision")] LibrarySettings model,
        CancellationToken ct)
    {
        if (!Request.HasFormContentType) return BadRequest("Submit the complete settings form.");
        var form = await Request.ReadFormAsync(ct);
        if (new[] { nameof(model.LibraryName), nameof(model.MaxActiveLoans), nameof(model.DefaultIssueDays),
            nameof(model.DailyFine), nameof(model.MembershipDurationMonths), nameof(model.Revision) }.Any(key => !form.ContainsKey(key)))
            return BadRequest("Submit the complete settings form.");
        if (ModelState.IsValid)
        {
            try
            {
                await settings.SaveAsync(model, Actor, ct);
                Success("Library settings saved.");
                return RedirectToAction(nameof(Index));
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        return View(model);
    }
}
