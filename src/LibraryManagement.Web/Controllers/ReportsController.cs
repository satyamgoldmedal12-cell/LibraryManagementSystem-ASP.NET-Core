using LibraryManagement.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace LibraryManagement.Web.Controllers;

[Authorize(Roles=Roles.Staff)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ReportsController(IReportService reports,ILibraryQueries queries) : LibraryController
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] ReportFilter filter,CancellationToken ct)
    {
        ReportResult? result=null;
        if(ModelState.IsValid){try{result=await reports.GetAsync(filter,false,ct);}catch(BusinessException ex){ModelState.AddModelError("",ex.Message);}}
        return View(await Model(filter,result,ct));
    }
    [HttpGet,EnableRateLimiting("report-export")]
    public async Task<IActionResult> Export([FromQuery] ReportFilter filter,string format,CancellationToken ct)
    {
        if(ModelState.IsValid)
        {
            try{var file=await reports.ExportAsync(filter,format,ct);return File(file.Content,file.ContentType,file.FileName);}
            catch(BusinessException ex){ModelState.AddModelError("",ex.Message);}
        }
        Response.StatusCode=StatusCodes.Status400BadRequest;
        return View("Index",await Model(filter,null,ct));
    }
    private async Task<ReportViewModel> Model(ReportFilter filter,ReportResult? result,CancellationToken ct)
    {
        result??=new(filter,ReportCatalog.All.FirstOrDefault(r=>r.Key==filter.Report)??ReportCatalog.All[0],new(),[],20);
        var book=filter.BookId is >0?await queries.BookAsync(filter.BookId.Value,ct):null;
        var member=filter.MemberId is >0?await queries.MemberAsync(filter.MemberId.Value,ct):null;
        return new(result,book is null?null:book.Title+" — "+book.ISBN,member is null?null:member.MemberNumber+" — "+member.FullName);
    }
}
