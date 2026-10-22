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

public sealed class ReturnFineTests
{
    [Theory]
    [InlineData(-3,0,0)] [InlineData(0,0,0)] [InlineData(1,1,7.25)] [InlineData(3,3,21.75)]
    public void Fine_uses_calendar_days_and_never_charges_on_or_before_due_date(int offset,int days,decimal amount)
    {
        var due = new DateTime(2026,10,20);
        var end = due.AddDays(offset).AddHours(23).AddMinutes(59);
        Assert.Equal(days,FineRules.LateDays(due,end));
        Assert.Equal(amount,FineRules.Calculate(due,end,7.25m));
    }

    [Fact] public void One_minute_across_UTC_midnight_is_one_late_calendar_day()
    {
        Assert.Equal(5m,FineRules.Calculate(new(2026,10,20,23,59,0),new(2026,10,21,0,0,0),5m));
        Assert.Equal(0m,FineRules.Calculate(new(2026,10,20),new(2026,10,25),0m));
    }

    [Theory] [InlineData(-1)] [InlineData(1000.01)] [InlineData(0.001)]
    public async Task Invalid_rates_do_not_reach_persistence(decimal rate)
    {
        var repo=new Mock<ICirculationRepository>(MockBehavior.Strict);
        var service=new CirculationService(repo.Object,Mock.Of<ILibraryService>(),new());
        await Assert.ThrowsAsync<BusinessException>(()=>service.SavePolicyAsync(new(){DailyFine=rate,Revision=1},"actor",default));
        repo.VerifyNoOtherCalls();
    }

    [Theory] [InlineData("returns")] [InlineData("fines")] [InlineData("collections")]
    public async Task Reversed_date_ranges_do_not_reach_reports(string kind)
    {
        var repo=new Mock<ICirculationRepository>(MockBehavior.Strict);
        var service=new CirculationService(repo.Object,Mock.Of<ILibraryService>(),new());
        await Assert.ThrowsAsync<BusinessException>(()=>service.ListAsync(kind,new(){FromDate=new(2026,10,22),ToDate=new(2026,10,21)},default));
        repo.VerifyNoOtherCalls();
    }

    [Theory] [InlineData(0,"actor")] [InlineData(1,"")]
    public async Task Return_and_payment_require_a_record_and_authenticated_actor(int id,string actor)
    {
        var commands=new Mock<ILibraryService>(MockBehavior.Strict);
        var service=new CirculationService(Mock.Of<ICirculationRepository>(),commands.Object,new());
        await Assert.ThrowsAsync<BusinessException>(()=>service.ReturnAsync(id,actor,default));
        await Assert.ThrowsAsync<BusinessException>(()=>service.PayFineAsync(id,actor,default));
        commands.VerifyNoOtherCalls();
    }
}
