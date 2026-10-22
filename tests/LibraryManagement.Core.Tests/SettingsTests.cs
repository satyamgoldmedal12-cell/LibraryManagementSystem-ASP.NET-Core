using LibraryManagement.Application.Interfaces;
using LibraryManagement.Application.DTOs;
using LibraryManagement.Application.Services;
using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Enums;
using LibraryManagement.Domain.Exceptions;
using LibraryManagement.Domain.Rules;
using Moq;
using Xunit;

namespace LibraryManagement.Core.Tests;

public sealed class SettingsTests
{
    private static LibrarySettings Valid() => new() { Revision = 1 };

    [Theory]
    [InlineData("name")][InlineData("nameLength")][InlineData("address")][InlineData("email")][InlineData("phone")]
    [InlineData("limitLow")][InlineData("limitHigh")][InlineData("daysLow")][InlineData("daysHigh")]
    [InlineData("monthsLow")][InlineData("monthsHigh")][InlineData("fineLow")][InlineData("fineHigh")]
    [InlineData("precision")][InlineData("revision")][InlineData("actor")]
    public async Task Invalid_settings_never_reach_persistence(string input)
    {
        var model = Valid(); var actor = "admin";
        switch (input)
        {
            case "name": model.LibraryName = "  "; break;
            case "nameLength": model.LibraryName = new string('L', 121); break;
            case "address": model.Address = new string('A', 501); break;
            case "email": model.Email = "invalid-email"; break;
            case "phone": model.Phone = "not-a-phone"; break;
            case "limitLow": model.MaxActiveLoans = 0; break;
            case "limitHigh": model.MaxActiveLoans = 101; break;
            case "daysLow": model.DefaultIssueDays = 0; break;
            case "daysHigh": model.DefaultIssueDays = 91; break;
            case "monthsLow": model.MembershipDurationMonths = 0; break;
            case "monthsHigh": model.MembershipDurationMonths = 121; break;
            case "fineLow": model.DailyFine = -0.01m; break;
            case "fineHigh": model.DailyFine = 1000.01m; break;
            case "precision": model.DailyFine = 0.001m; break;
            case "revision": model.Revision = 0; break;
            case "actor": actor = " "; break;
        }
        var repo = new Mock<ILibrarySettingsRepository>(MockBehavior.Strict);
        await Assert.ThrowsAsync<BusinessException>(() => new LibrarySettingsService(repo.Object, new()).SaveAsync(model, actor, default));
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Saving_normalizes_contact_information_and_allows_zero_fine()
    {
        var model = Valid(); model.LibraryName = "  City Library  "; model.Address = "  Main Road  ";
        model.Email = "  desk@example.test  "; model.Phone = "  +91 9876543210  "; model.DailyFine = 0;
        var repo = new Mock<ILibrarySettingsRepository>();
        await new LibrarySettingsService(repo.Object, new()).SaveAsync(model, "admin", default);
        repo.Verify(x => x.SaveAsync(It.Is<LibrarySettings>(s => s.LibraryName == "City Library" && s.Address == "Main Road"
            && s.Email == "desk@example.test" && s.Phone == "+91 9876543210" && s.DailyFine == 0), "admin", default), Times.Once);
    }

    [Fact]
    public async Task Reads_preserve_server_fallback_options()
    {
        var defaults = new LendingOptions { MaxActiveLoans = 8, DailyFine = 3.5m };
        var repo = new Mock<ILibrarySettingsRepository>(); var settings = Valid();
        repo.Setup(x => x.GetAsync(defaults, default)).ReturnsAsync(settings);
        Assert.Same(settings, await new LibrarySettingsService(repo.Object, defaults).GetAsync(default));
    }

    [Fact]
    public async Task Issue_assessment_uses_saved_limit_duration_and_fine()
    {
        var context = new IssueContext { BookId = 1, MemberId = 2, AvailableCopies = 2, MemberActive = true,
            MemberExpiresOn = new DateTime(2027, 1, 1), ActiveLoans = 2, ConfiguredMaxActiveLoans = 2, DefaultIssueDays = 7, ConfiguredDailyFine = 7.25m };
        var repo = new Mock<ILibraryRepository>(); repo.Setup(x => x.IssueContextAsync(1, 2, default)).ReturnsAsync(context);
        var service = new BookIssueService(repo.Object, Mock.Of<ILibraryService>(), new(), new Clock());
        var assessment = await service.AssessAsync(1, 2, null, default);
        Assert.Equal(2, assessment.MaximumBooks); Assert.Equal(7.25m, assessment.DailyFine);
        Assert.Equal(new DateTime(2026, 10, 29), assessment.DueDate); Assert.False(assessment.CanIssue);
        context.ActiveLoans = 1; context.MemberExpiresOn = new DateTime(2026, 10, 25);
        assessment = await service.AssessAsync(1, 2, null, default);
        Assert.True(assessment.CanIssue); Assert.Equal(new DateTime(2026, 10, 25), assessment.DueDate);
    }

    private sealed class Clock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero); }
}
