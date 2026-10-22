using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagement.Web.Controllers;

[Authorize(Roles = Roles.Staff)]
public sealed class CategoriesController(ILibraryQueries queries, ILibraryService service) : LibraryController
{
    public async Task<IActionResult> Index(CancellationToken ct) => View(await queries.CategoriesAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create(Category category, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await service.SaveCategoryAsync(category, Actor, ct);
                Success("Category added.");
                return RedirectToAction(nameof(Index));
            }
            catch (BusinessException ex)
            {
                ModelState.AddModelError("", ex.Message);
            }
        }

        return View("Index", await queries.CategoriesAsync(ct));
    }
}
