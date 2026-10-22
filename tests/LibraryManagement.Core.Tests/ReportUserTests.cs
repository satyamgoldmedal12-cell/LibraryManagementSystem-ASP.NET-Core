using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Enums;
using LibraryManagement.Domain.Exceptions;
using LibraryManagement.Domain.Rules;
using LibraryManagement.Application.Interfaces;
using LibraryManagement.Application.DTOs;
using LibraryManagement.Application.Services;
using Moq;
using Xunit;
namespace LibraryManagement.Core.Tests;
public sealed class ReportUserTests
{
    private sealed class Clock : TimeProvider {public override DateTimeOffset GetUtcNow()=>new(2026,10,22,10,0,0,TimeSpan.Zero);}
    [Theory] [InlineData("unknown","all")] [InlineData("stock","paid")] [InlineData("monthly","active")]
    public async Task Invalid_report_or_status_never_reaches_SQL(string report,string status)
    {
        var repo=new Mock<IReportRepository>(MockBehavior.Strict);var service=new ReportService(repo.Object,Mock.Of<IReportExporter>(),new Clock());
        await Assert.ThrowsAsync<BusinessException>(()=>service.GetAsync(new(){Report=report,Status=status},false,default));repo.VerifyNoOtherCalls();
    }
    [Fact] public async Task Reversed_dates_and_excessive_month_ranges_are_rejected()
    {
        var repo=new Mock<IReportRepository>(MockBehavior.Strict);var service=new ReportService(repo.Object,Mock.Of<IReportExporter>(),new Clock());
        await Assert.ThrowsAsync<BusinessException>(()=>service.GetAsync(new(){FromDate=new(2026,10,23),ToDate=new(2026,10,22)},false,default));
        await Assert.ThrowsAsync<BusinessException>(()=>service.GetAsync(new(){Report="monthly",FromDate=new(2000,1,1),ToDate=new(2026,10,22)},false,default));repo.VerifyNoOtherCalls();
    }
    [Fact] public async Task Monthly_defaults_include_twelve_calendar_months()
    {
        var repo=new Mock<IReportRepository>();repo.Setup(r=>r.QueryAsync(It.IsAny<ReportFilter>(),20,default)).ReturnsAsync(new ReportData([],new()));
        var result=await new ReportService(repo.Object,Mock.Of<IReportExporter>(),new Clock()).GetAsync(new(){Report="monthly"},false,default);
        Assert.Equal(new DateTime(2025,11,1),result.Filter.FromDate);Assert.Equal(new DateTime(2026,10,22),result.Filter.ToDate);
    }
    [Fact] public async Task Exports_reject_oversized_results_without_silent_truncation()
    {
        var repo=new Mock<IReportRepository>();repo.Setup(r=>r.QueryAsync(It.IsAny<ReportFilter>(),5001,default)).ReturnsAsync(new ReportData([],new(){TotalRecords=5001}));var exporter=new Mock<IReportExporter>(MockBehavior.Strict);
        await Assert.ThrowsAsync<BusinessException>(()=>new ReportService(repo.Object,exporter.Object,new Clock()).ExportAsync(new(){Page=7},"excel",default));repo.Verify(r=>r.QueryAsync(It.Is<ReportFilter>(f=>f.Page==1),5001,default));exporter.VerifyNoOtherCalls();
    }
    [Fact] public async Task Invalid_export_format_is_rejected_before_querying()
    {
        var repo=new Mock<IReportRepository>(MockBehavior.Strict);await Assert.ThrowsAsync<BusinessException>(()=>new ReportService(repo.Object,Mock.Of<IReportExporter>(),new Clock()).ExportAsync(new(),"html",default));repo.VerifyNoOtherCalls();
    }
    [Fact] public async Task Self_demotion_does_not_reach_identity_writes()
    {
        var identity=new Mock<IUserAccountIdentity>(MockBehavior.Strict);identity.Setup(x=>x.SummaryAsync("actor",default)).ReturnsAsync(new UserSummary{Id="actor",Role=Roles.Admin});
        var result=await new UserAdministrationService(identity.Object,Mock.Of<ILibraryRepository>()).EditAsync(new(){Id="actor",FullName="Admin",Email="admin@example.test",Role=Roles.Librarian,Revision=1},"actor",default);
        Assert.False(result.Succeeded);identity.VerifyAll();identity.VerifyNoOtherCalls();
    }
    [Fact] public async Task Invalid_edit_and_mismatched_membership_are_rejected()
    {
        var identity=new Mock<IUserAccountIdentity>();identity.Setup(x=>x.SummaryAsync("target",default)).ReturnsAsync(new UserSummary{Id="target",Role=Roles.Librarian});var repo=new Mock<ILibraryRepository>();repo.Setup(x=>x.MemberAsync(17,default)).ReturnsAsync(new Member{Email="different@example.test"});
        var result=await new UserAdministrationService(identity.Object,repo.Object).EditAsync(new(){Id="target",FullName="Reader",Email="reader@example.test",Role=Roles.Member,MemberId=17,Revision=1},"actor",default);Assert.False(result.Succeeded);identity.Verify(x=>x.EditAsync(It.IsAny<EditUserModel>(),It.IsAny<string>(),default),Times.Never);
    }
}
