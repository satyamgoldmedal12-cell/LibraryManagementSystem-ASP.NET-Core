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
public sealed class BookIssueTests
{
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026,10,21,12,0,0,TimeSpan.Zero); }
    private static IssueContext Valid() => new() { BookId=1,MemberId=2,TotalCopies=2,AvailableCopies=2,MemberActive=true,MemberExpiresOn=new(2027,10,21) };
    private static BookIssueService Service(IssueContext context) { var repo=new Mock<ILibraryRepository>();repo.Setup(x=>x.IssueContextAsync(1,2,default)).ReturnsAsync(context);return new(repo.Object,Mock.Of<ILibraryService>(),new(),new Clock()); }
    [Fact] public async Task Suggested_due_date_respects_membership_expiry()
    { var c=Valid();c.MemberExpiresOn=new(2026,10,24);var a=await Service(c).AssessAsync(1,2,null,default);Assert.True(a.CanIssue);Assert.Equal(new DateTime(2026,10,24),a.DueDate);Assert.Equal(new DateTime(2026,10,21,17,30,0),a.IssueDateUtc); }
    [Theory] [InlineData("stock")] [InlineData("archived")] [InlineData("inactive")] [InlineData("deleted")] [InlineData("expired")] [InlineData("duplicate")] [InlineData("limit")] [InlineData("overdue")] [InlineData("missingBook")] [InlineData("missingMember")]
    public async Task Ineligible_issues_are_explained(string condition)
    { var c=Valid();switch(condition){case "stock":c.AvailableCopies=0;break;case "archived":c.BookArchived=true;break;case "inactive":c.MemberActive=false;break;case "deleted":c.MemberArchived=true;break;case "expired":c.MemberExpiresOn=new(2026,10,20);break;case "duplicate":c.DuplicateActiveIssue=true;break;case "limit":c.ActiveLoans=5;break;case "overdue":c.HasOverdueLoans=true;break;case "missingBook":c.BookId=null;break;case "missingMember":c.MemberId=null;break;}var a=await Service(c).AssessAsync(1,2,null,default);Assert.False(a.CanIssue);Assert.NotEmpty(a.Problems); }
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(91)]
    public async Task Due_date_must_be_tomorrow_through_ninety_days(int days)
    { Assert.False((await Service(Valid()).AssessAsync(1,2,new DateTime(2026,10,21).AddDays(days),default)).CanIssue); }
    [Fact] public async Task Explicit_due_date_cannot_exceed_membership_expiry()
    { var c=Valid();c.MemberExpiresOn=new(2026,10,24);Assert.False((await Service(c).AssessAsync(1,2,new DateTime(2026,10,25),default)).CanIssue); }
}
