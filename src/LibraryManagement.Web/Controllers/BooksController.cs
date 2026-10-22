using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.RateLimiting;

namespace LibraryManagement.Web.Controllers;

public sealed class BooksController(ILibraryQueries queries, IBookManagementService books) : LibraryController
{
    private bool Staff => User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Librarian);

    public async Task<IActionResult> Index(string? search, int page = 1, bool includeArchived = false, CancellationToken ct = default)
    {
        includeArchived &= Staff;
        return View(await queries.BooksAsync(SearchTerm(search), PageNumber(page), includeArchived, ct));
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var book = await queries.BookAsync(id, ct);
        if (book is null || (book.IsArchived && !Staff)) return NotFound();
        return View(book);
    }

    [HttpGet]
    public async Task<IActionResult> Cover(int id, CancellationToken ct)
    {
        var book = await queries.BookAsync(id, ct);
        if (book is null || !book.HasCover || (book.IsArchived && !Staff)) return NotFound();
        var cover = await queries.BookCoverAsync(id, ct);
        return cover is null ? NotFound() : File(cover.Content, "image/jpeg");
    }
    [Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> Edit(int id = 0, CancellationToken ct = default)
    {
        var book = id == 0 ? new Book() : await queries.BookAsync(id, ct);
        if (book is null) return NotFound();

        await Categories(ct);
        return View(book);
    }

    [HttpPost, Authorize(Roles = Roles.Staff), EnableRateLimiting("book-management")]
    [RequestSizeLimit(3 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 3 * 1024 * 1024)]
    public async Task<IActionResult> Edit(int id, Book book, IFormFile? coverImage, bool removeCover, CancellationToken ct)
    {
        if (id != book.Id) return BadRequest();
        // Accept common printed ISBN formatting while keeping the domain/database value canonical.
        book.ISBN = (book.ISBN ?? "").Replace("-", "").Replace(" ", "");
        ModelState.Remove(nameof(book.ISBN));
        TryValidateModel(book);
        BookCoverUpload? upload = null;
        if (coverImage is not null)
        {
            if (coverImage.Length is 0 or > BookCoverLimits.MaxUploadBytes)
                ModelState.AddModelError("coverImage", "Choose a JPEG or PNG cover up to 2 MB.");
            else if (ModelState.IsValid)
            {
                await using var input = coverImage.OpenReadStream();
                using var buffer = new MemoryStream();
                var chunk = new byte[8192];
                int count;

                while ((count = await input.ReadAsync(chunk, ct)) > 0)
                {
                    if (buffer.Length + count > BookCoverLimits.MaxUploadBytes)
                    {
                        ModelState.AddModelError("coverImage", "Cover images must not exceed 2 MB.");
                        break;
                    }

                    await buffer.WriteAsync(chunk.AsMemory(0, count), ct);
                }

                if (ModelState.IsValid) upload = new(buffer.ToArray(), coverImage.FileName);
            }
        }

        if (ModelState.IsValid)
        {
            try
            {
                await books.SaveAsync(book, upload, removeCover, Actor, ct);
                Success("Book saved.");
                return RedirectToAction(nameof(Index));
            }
            catch (BusinessException ex)
            {
                ModelState.AddModelError("", ex.Message);
            }
        }

        // Read display-only values from persistence rather than trusting form fields.
        if (id > 0)
        {
            var current = await queries.BookAsync(id, ct);
            if (current is null) return NotFound();

            book.HasCover = current.HasCover;
            book.AvailableCopies = current.AvailableCopies;
            book.CategoryName = current.CategoryName;
        }
        else
        {
            book.HasCover = false;
        }

        ViewBag.RemoveCover = removeCover;

        await Categories(ct);
        return View(book);
    }

    [HttpGet, Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var book = await queries.BookAsync(id, ct);
        return book is null ? NotFound() : View(book);
    }

    [HttpPost, ActionName("Delete"), Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> DeleteConfirmed(int id, int revision, CancellationToken ct)
    {
        try
        {
            await books.DeleteAsync(id, revision, Actor, ct);
            Success("Book deleted from the active catalog. Its history is preserved; staff can restore it from archived books.");
            return RedirectToAction(nameof(Index));
        }
        catch (BusinessException ex)
        {
            ModelState.AddModelError("", ex.Message);
        }

        var book = await queries.BookAsync(id, ct);
        return book is null ? NotFound() : View("Delete", book);
    }

    private async Task Categories(CancellationToken ct)
    {
        ViewBag.Categories = new SelectList(await queries.CategoriesAsync(ct), "Id", "Name");
    }

    [Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> Lookup(string? search, CancellationToken ct, bool includeArchived = false)
    {
        if (string.IsNullOrWhiteSpace(search) || search.Trim().Length < 2) return Json(Array.Empty<object>());

        var results = await queries.BooksAsync(SearchTerm(search), 1, includeArchived, ct);

        return Json(results.Items.Select(x => new
        {
            x.Id,
            text = $"{x.Title} — {x.ISBN} ({x.AvailableCopies} available)",
            disabled = !includeArchived && x.AvailableCopies < 1
        }));
    }
}
