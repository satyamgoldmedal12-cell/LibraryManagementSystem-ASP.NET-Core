using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LibraryManagement.Web.ViewModels;

namespace LibraryManagement.Web.Controllers;

public sealed class LoansController(ILibraryQueries queries, ILibraryService service, IBookIssueService issues) : LibraryController
{
    public async Task<IActionResult> Index(string? search, string status = "active", int page = 1, CancellationToken ct = default)
    {
        if (status is not ("active" or "returned" or "overdue" or "unpaid" or "all")) status = "active";

        int? memberId = null;
        if (User.IsInRole(Roles.Member))
        {
            if (!int.TryParse(User.FindFirst("member_id")?.Value, out var id)) return Forbid();
            memberId = id; // Taken from the signed cookie, never from query string or form.
        }

        return View(await queries.LoansAsync(SearchTerm(search), status, memberId, PageNumber(page), ct));
    }

    [HttpGet, Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> Issue(int bookId=0,int memberId=0,CancellationToken ct=default)
    {
        var assessment=await issues.AssessAsync(bookId,memberId,null,ct);
        return View(new IssueBookViewModel(new IssueLoan { BookId=bookId,MemberId=memberId,DueDate=assessment.DueDate },assessment));
    }
    [HttpPost, Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> Issue(IssueLoan loan,CancellationToken ct)
    {
        if(ModelState.IsValid)
        {
            try
            {
                var id=await issues.IssueAsync(loan,Actor,ct);
                Success("Book issued successfully.");return RedirectToAction(nameof(Receipt),new { id });
            }
            catch(BusinessException ex){ModelState.AddModelError("",ex.Message);}
        }
        return View(new IssueBookViewModel(loan,await issues.AssessAsync(loan.BookId,loan.MemberId,loan.DueDate,ct)));
    }
    [HttpGet, Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> Availability(int bookId=0,int memberId=0,DateTime? dueDate=null,CancellationToken ct=default)
    {
        if(!ModelState.IsValid)return BadRequest(new { message="Check the selected dates and identifiers." });
        var result=await issues.AssessAsync(bookId,memberId,dueDate,ct);var context=result.Context;
        return Json(new { result.CanIssue,messages=result.Problems,bookFound=context.BookId.HasValue,memberFound=context.MemberId.HasValue,
            bookLabel=context.BookId.HasValue?$"{context.BookTitle} — {context.ISBN}":null,
            memberLabel=context.MemberId.HasValue?$"{context.MemberNumber} — {context.MemberName}":null,
            context.AvailableCopies,context.TotalCopies,context.ActiveLoans,maxBooks=result.MaximumBooks,
            expiresOn=context.MemberExpiresOn?.ToString("yyyy-MM-dd"),minimumDueDate=result.MinimumDueDate.ToString("yyyy-MM-dd"),
            maximumDueDate=result.MaximumDueDate.ToString("yyyy-MM-dd"),suggestedDueDate=result.DueDate.ToString("yyyy-MM-dd") });
    }
    [HttpGet]
    public async Task<IActionResult> Receipt(int id,CancellationToken ct)
    {
        var receipt=await issues.ReceiptAsync(id,ct);
        if(receipt is null)return NotFound();
        if(!User.IsInRole(Roles.Admin)&&!User.IsInRole(Roles.Librarian)
            &&(!User.IsInRole(Roles.Member)||!int.TryParse(User.FindFirst("member_id")?.Value,out var ownId)||ownId!=receipt.MemberId))return NotFound();
        return View(receipt);
    }
    [HttpGet, Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> History(string? search,string status="all",int page=1,CancellationToken ct=default)
    {
        if(status is not ("all" or "active" or "returned" or "overdue"))status="all";
        return View(await queries.LoansAsync(SearchTerm(search),status,null,PageNumber(page),ct));
    }

    [HttpPost, Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> Return(int id, CancellationToken ct)
    {
        try
        {
            await service.ReturnAsync(id, Actor, ct);
            Success("Book returned. The final fine has been calculated.");
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> PayFine(int id, CancellationToken ct)
    {
        try
        {
            await service.PayFineAsync(id, Actor, ct);
            Success("Fine payment recorded.");
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { status = "unpaid" });
    }
}
