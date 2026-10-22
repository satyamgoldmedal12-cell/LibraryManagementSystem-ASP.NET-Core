using Microsoft.AspNetCore.Identity;

namespace LibraryManagement.Infrastructure.Identity;

internal static class IdentityResults
{
    public static OperationResult ToOperationResult(this IdentityResult result) => result.Succeeded
        ? OperationResult.Success : new(false, result.Errors.Select(e => new ServiceError(e.Code, e.Description)).ToArray());
}
