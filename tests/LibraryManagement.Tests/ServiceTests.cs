using System.Security.Claims;
using LibraryManagement.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace LibraryManagement.Tests;

public sealed class ServiceTests
{
    [Fact]
    public async Task Member_can_only_query_own_loans()
    {
        var repo = new Mock<ILibraryQueries>();
        repo.Setup(x => x.LoansAsync(null, "all", 17, 1, default)).ReturnsAsync(new PagedResult<Loan>([], 0, 1, 12));
        var controller = new LoansController(repo.Object, Mock.Of<ILibraryService>(), Mock.Of<IBookIssueService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Member), new Claim("member_id", "17")], "test")) } }
        };
        Assert.IsType<ViewResult>(await controller.Index(null, "all", 1));
        repo.Verify(x => x.LoansAsync(null, "all", 17, 1, default), Times.Once);
    }
    [Fact]
    public async Task Missing_member_link_fails_closed()
    {
        var controller = new LoansController(Mock.Of<ILibraryQueries>(), Mock.Of<ILibraryService>(), Mock.Of<IBookIssueService>())
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Member)], "test")) } } };
        Assert.IsType<ForbidResult>(await controller.Index(null));
    }
}
