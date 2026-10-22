using LibraryManagement.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagement.Web.Controllers;

[Authorize(Roles = Roles.Admin)]
public sealed class ModulePathsController(IModuleAccessService permissions) : LibraryController
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken ct = default)
    {
        var catalog = await permissions.ModulesAsync(ct);
        var term = SearchTerm(search);

        var rows = ModuleHierarchy
            .Ordered(catalog)
            .Where(m =>
                term is null
                || ModuleHierarchy.FullPath(m, catalog).Contains(term, StringComparison.OrdinalIgnoreCase)
                || (m.ControllerName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToArray();

        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(rows.Length / 25d)));

        return View(new ModulePathListViewModel
        {
            Search = term,
            Page = page,
            Total = rows.Length,
            Rows = rows.Skip((page - 1) * 25).Take(25).ToArray(),
            Catalog = catalog
        });
    }

    [HttpGet]
    public async Task<IActionResult> GridData(string? search, int skip = 0, int take = 25, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var catalog = await permissions.ModulesAsync(ct);
        var term = SearchTerm(search);

        var rows = ModuleHierarchy
            .Ordered(catalog)
            .Where(m =>
                term is null
                || ModuleHierarchy.FullPath(m, catalog).Contains(term, StringComparison.OrdinalIgnoreCase)
                || (m.ControllerName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToArray();

        var items = rows
            .Skip(skip)
            .Take(take)
            .Select(m => new
            {
                m.Id,
                m.Revision,
                m.DisplayName,
                FullPath = ModuleHierarchy.FullPath(m, catalog),
                m.ControllerName,
                m.PagePath,
                m.IsLeafNode,
                m.LabelId,
                m.SortOrder,
                m.IsActive,
                IsDeleted = m.DeletedDate.HasValue,
                Rights = m.IsLeafNode ? m.Rights.ToString() : "—"
            });

        return Json(new { data = items, totalCount = rows.Length });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id = 0, CancellationToken ct = default)
    {
        await Options(ct);

        if (id == 0)
        {
            return View(new ModulePath { IsRead = true });
        }

        var module = (await permissions.ModulesAsync(ct)).SingleOrDefault(x => x.Id == id);
        return module is null ? NotFound() : View(module);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, ModulePath model, CancellationToken ct)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        if (ModelState.IsValid)
        {
            try
            {
                await permissions.SaveModuleAsync(model, Actor, ct);
                Success("Module saved. Menu and permissions apply on the next request.");
                return RedirectToAction(nameof(Index));
            }
            catch (BusinessException ex)
            {
                ModelState.AddModelError("", ex.Message);
            }
        }

        await Options(ct);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, int revision, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest();
        }

        try
        {
            await permissions.DeleteModuleAsync(id, revision, Actor, ct);
            Success("Module removed from menus. Audit history and grants are retained.");
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task Options(CancellationToken ct)
    {
        ViewBag.Modules = await permissions.ModulesAsync(ct);
    }
}
