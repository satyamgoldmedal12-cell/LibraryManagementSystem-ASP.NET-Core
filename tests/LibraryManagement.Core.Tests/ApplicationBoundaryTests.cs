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

public sealed class ApplicationBoundaryTests
{
    [Fact]
    public void Core_assemblies_depend_only_on_BCL_and_inward_layers()
    {
        Assert.All(typeof(Book).Assembly.GetReferencedAssemblies(), a => Assert.True(
            a.Name == "System" || a.Name!.StartsWith("System.", StringComparison.Ordinal), a.FullName));
        Assert.All(typeof(ILibraryService).Assembly.GetReferencedAssemblies(), a => Assert.True(
            a.Name == "LibraryManagement.Domain" || a.Name == "System" || a.Name!.StartsWith("System.", StringComparison.Ordinal), a.FullName));
    }
    [Fact]
    public async Task Registration_validates_before_calling_external_identity()
    {
        var gateway = new Mock<IMemberAccountProvisioner>(MockBehavior.Strict);
        var result = await new MemberRegistrationService(gateway.Object).RegisterAsync(new RegisterModel());
        Assert.False(result.Succeeded);
        gateway.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Registration_normalizes_details_through_an_identity_independent_port()
    {
        var gateway = new Mock<IMemberAccountProvisioner>(MockBehavior.Strict);
        gateway.Setup(x => x.CreateAsync("Reader", "reader@example.test", "Strong!Password123")).ReturnsAsync(OperationResult.Success);
        var result = await new MemberRegistrationService(gateway.Object).RegisterAsync(new RegisterModel
        {
            FullName = " Reader ",
            Email = "Reader@Example.Test",
            Password = "Strong!Password123",
            ConfirmPassword = "Strong!Password123"
        });
        Assert.True(result.Succeeded); gateway.VerifyAll();
    }
    [Fact]
    public async Task Mismatched_member_email_never_reaches_account_creation()
    {
        var gateway = new Mock<IUserAccountIdentity>(MockBehavior.Strict);
        var repo = new Mock<ILibraryRepository>();
        repo.Setup(x => x.MemberAsync(17, default)).ReturnsAsync(new Member { Email = "someoneelse@example.test" });
        var result = await new UserAdministrationService(gateway.Object, repo.Object).CreateAsync(new CreateUserModel
        {
            FullName = "Reader",
            Email = "reader@example.test",
            Password = "Strong!Password123",
            Role = Roles.Member,
            MemberId = 17
        });
        Assert.False(result.Succeeded); gateway.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Administrator_cannot_disable_own_account_at_application_boundary()
    {
        var gateway = new Mock<IUserAccountIdentity>(MockBehavior.Strict);
        var result = await new UserAdministrationService(gateway.Object, Mock.Of<ILibraryRepository>()).SetAccessAsync("actor", false, "actor", default);
        Assert.False(result.Succeeded); gateway.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Session_listing_uses_server_policy_through_repository_port()
    {
        var repo = new Mock<IUserSessionRepository>(MockBehavior.Strict);
        repo.Setup(x => x.ListAsync("actor", 19, default)).ReturnsAsync([]);
        await new UserSessionService(repo.Object, new AuthenticationSessionOptions { IdleMinutes = 19 }).ListAsync("actor", default);
        repo.VerifyAll();
    }
}
