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

public sealed class MemberManagementTests
{
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero); }
    private static Member Member() => new() { MemberNumber = " lib-1 ", FullName = " Reader ", Email = "Reader@EXAMPLE.TEST", Phone = "9876543210", Address = " Street 1 ", JoinedOn = new(2026, 10, 20), ExpiresOn = new(2027, 10, 21) };
    [Theory]
    [InlineData(1, 365)]
    [InlineData(0, -1)]
    [InlineData(-30, -1)]
    public async Task Invalid_membership_dates_never_reach_persistence(int joinedOffset, int expiryOffset)
    {
        var repo = new Mock<ILibraryRepository>(MockBehavior.Strict); var member = Member();
        member.JoinedOn = new DateTime(2026, 10, 21).AddDays(joinedOffset); member.ExpiresOn = new DateTime(2026, 10, 21).AddDays(expiryOffset);
        await Assert.ThrowsAsync<BusinessException>(() => new MemberManagementService(repo.Object, new Clock()).SaveAsync(member, "actor", default)); repo.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Details_are_normalized_at_the_application_boundary()
    {
        var repo = new Mock<ILibraryRepository>(MockBehavior.Strict); var member = Member();
        repo.Setup(x => x.SaveMemberAsync(member, "actor", default)).ReturnsAsync(42);
        Assert.Equal(42, await new MemberManagementService(repo.Object, new Clock()).SaveAsync(member, "actor", default));
        Assert.Equal("LIB-1", member.MemberNumber); Assert.Equal("reader@example.test", member.Email); Assert.Equal("Street 1", member.Address); repo.VerifyAll();
    }
    [Fact]
    public async Task Missing_profile_does_not_query_other_data()
    {
        var repo = new Mock<ILibraryRepository>(MockBehavior.Strict); repo.Setup(x => x.MemberAsync(17, default)).ReturnsAsync((Member?)null);
        Assert.Null(await new MemberManagementService(repo.Object, new Clock()).ProfileAsync(17, 1, default)); repo.VerifyAll(); repo.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Delete_requires_an_authenticated_actor()
    {
        var repo = new Mock<ILibraryRepository>(MockBehavior.Strict);
        await Assert.ThrowsAsync<BusinessException>(() => new MemberManagementService(repo.Object, new Clock()).DeleteAsync(17, 1, "", default)); repo.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Restore_uses_the_expected_revision()
    {
        var repo = new Mock<ILibraryRepository>(MockBehavior.Strict); repo.Setup(x => x.RestoreMemberAsync(17, 4, "actor", default)).Returns(Task.CompletedTask);
        await new MemberManagementService(repo.Object, new Clock()).RestoreAsync(17, 4, "actor", default); repo.VerifyAll();
    }
}
