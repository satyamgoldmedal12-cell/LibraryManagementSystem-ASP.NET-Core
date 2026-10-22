using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagement.Web.Controllers;

public sealed class MembersController(IMemberManagementService members, ILibrarySettingsService settings) : LibraryController
{
    private bool Staff => User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Librarian);

    [Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> Index(string? search, int page = 1, bool includeArchived = false, string status = "all", CancellationToken ct = default) =>
        View(await members.ListAsync(SearchTerm(search), PageNumber(page), includeArchived, status, ct));

    public async Task<IActionResult> Profile(int id = 0, int page = 1, CancellationToken ct = default)
    {
        if (!Staff)
        {
            if (!User.IsInRole(Roles.Member) || !int.TryParse(User.FindFirst("member_id")?.Value, out var ownId))
            {
                return Forbid();
            }

            if (id != 0 && id != ownId)
            {
                return Forbid();
            }

            id = ownId;
        }

        var profile = await members.ProfileAsync(id, PageNumber(page), ct);
        return profile is null || (!Staff && profile.Member.IsArchived) ? NotFound() : View(profile);
    }

    [Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> Edit(int id = 0, CancellationToken ct = default)
    {
        var member = id == 0
            ? new Member
            {
                JoinedOn = LibraryManagement.Domain.Rules.IndiaTime.Today,
                ExpiresOn = LibraryManagement.Domain.Rules.IndiaTime.Today.AddMonths((await settings.GetAsync(ct)).MembershipDurationMonths),
                MemberNumber = "LIB-" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant()
            }
            : await members.GetAsync(id, ct);

        return member is null ? NotFound() : View(member);
    }

    [HttpPost, Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> Edit(int id, Member member, CancellationToken ct)
    {
        if (id != member.Id) return BadRequest();

        if (ModelState.IsValid)
        {
            try
            {
                var savedId = await members.SaveAsync(member, Actor, ct);
                Success("Member saved.");
                return RedirectToAction(nameof(Profile), new { id = savedId });
            }
            catch (BusinessException ex)
            {
                ModelState.AddModelError("", ex.Message);
            }
        }

        if (id > 0)
        {
            var current = await members.GetAsync(id, ct);
            if (current is null) return NotFound();

            member.IsArchived = current.IsArchived;
        }
        else
        {
            member.IsArchived = false;
        }

        return View(member);
    }

    [HttpGet, Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var profile = await members.ProfileAsync(id, 1, ct);
        return profile is null ? NotFound() : View(profile);
    }

    [HttpPost, ActionName("Delete"), Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> DeleteConfirmed(int id, int revision, CancellationToken ct)
    {
        try
        {
            await members.DeleteAsync(id, revision, Actor, ct);
            Success("Member deleted. History is preserved and linked sessions are signed out.");
            return RedirectToAction(nameof(Index));
        }
        catch (BusinessException ex)
        {
            ModelState.AddModelError("", ex.Message);
        }

        var profile = await members.ProfileAsync(id, 1, ct);
        return profile is null ? NotFound() : View("Delete", profile);
    }

    [HttpPost, Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> Restore(int id, int revision, CancellationToken ct)
    {
        try
        {
            await members.RestoreAsync(id, revision, Actor, ct);
            Success("Member restored as inactive. Edit the membership to activate or renew it.");
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Profile), new { id });
    }

    [Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> Lookup(string? search, CancellationToken ct, bool includeArchived = false)
    {
        if (string.IsNullOrWhiteSpace(search) || search.Trim().Length < 2) return Json(Array.Empty<object>());

        var results = await members.ListAsync(SearchTerm(search), 1, includeArchived, includeArchived ? "all" : "active", ct);
        return Json(results.Items.Select(x => new { x.Id, text = $"{x.MemberNumber} — {x.FullName}" }));
    }
}
