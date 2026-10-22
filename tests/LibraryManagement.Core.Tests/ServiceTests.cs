using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Enums;
using LibraryManagement.Domain.Exceptions;
using LibraryManagement.Domain.Rules;
using LibraryManagement.Application.DTOs;
using LibraryManagement.Application.Interfaces;
using LibraryManagement.Application.Services;
using Moq;
using Xunit;

namespace LibraryManagement.Core.Tests;

public sealed class ServiceTests
{
    private sealed class FrozenClock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero); }
    private static LibraryService Service(ILibraryRepository repository) => new(repository, new LendingOptions(), new FrozenClock());

    [Theory]
    [InlineData("9780132350884", true)]
    [InlineData("9780201633610", true)]
    [InlineData("9780132350885", false)]
    [InlineData("978013235088", false)]
    [InlineData("978013235088X", false)]
    public void Isbn_checksum_is_validated(string isbn, bool expected) => Assert.Equal(expected, LibraryService.ValidIsbn(isbn));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(91)]
    public async Task Invalid_due_dates_never_reach_repository(int offset)
    {
        var repo = new Mock<ILibraryRepository>(MockBehavior.Strict);
        var loan = new IssueLoan { BookId = 1, MemberId = 2, DueDate = new DateTime(2026, 10, 21).AddDays(offset) };
        await Assert.ThrowsAsync<BusinessException>(() => Service(repo.Object).IssueAsync(loan, "staff", default));
        repo.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Lending_policy_comes_from_server_configuration()
    {
        var repo = new Mock<ILibraryRepository>();
        var loan = new IssueLoan { BookId = 1, MemberId = 2, DueDate = new DateTime(2026, 10, 22) };
        repo.Setup(x => x.IssueAsync(loan, It.Is<LendingOptions>(p => p.MaxActiveLoans == 5 && p.DailyFine == 5), "staff", default)).ReturnsAsync(42);
        Assert.Equal(42, await Service(repo.Object).IssueAsync(loan, "staff", default));
        repo.VerifyAll();
    }
    [Fact]
    public async Task Expired_new_membership_is_rejected()
    {
        var repo = new Mock<ILibraryRepository>(MockBehavior.Strict);
        var member = new Member { MemberNumber = "M-1", FullName = "Reader", Email = "reader@example.test", ExpiresOn = new DateTime(2026, 10, 20) };
        await Assert.ThrowsAsync<BusinessException>(() => Service(repo.Object).SaveMemberAsync(member, "staff", default));
    }
}
