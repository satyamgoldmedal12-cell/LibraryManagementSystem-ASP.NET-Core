using LibraryManagement.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagement.Web.Controllers;

public abstract class CirculationControllerBase(ICirculationService circulation, ILibraryQueries queries) : LibraryController
{
    protected ICirculationService Circulation { get; } = circulation;

    protected async Task<CirculationLedgerViewModel> Ledger(string kind, CirculationFilter filter, CancellationToken ct)
    {
        CirculationPage result = new(new([],0,Math.Clamp(filter.Page,1,100000),12),new());
        if (ModelState.IsValid)
        {
            try { result = await Circulation.ListAsync(kind, filter, ct); }
            catch (BusinessException ex) { ModelState.AddModelError("",ex.Message); }
        }
        var book = filter.BookId is > 0 ? await queries.BookAsync(filter.BookId.Value,ct) : null;
        var member = filter.MemberId is > 0 ? await queries.MemberAsync(filter.MemberId.Value,ct) : null;
        return new(filter,result,kind,book is null ? null : book.Title + " — " + book.ISBN,
            member is null ? null : member.MemberNumber + " — " + member.FullName);
    }
}
