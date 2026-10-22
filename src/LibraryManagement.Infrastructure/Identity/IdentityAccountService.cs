using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace LibraryManagement.Infrastructure.Identity;

public sealed class IdentityAccountService(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users,
    IUserSessionRepository sessions, ILibraryRepository members, IHttpContextAccessor accessor, ILogger<IdentityAccountService> logger, IUserAccountIdentity activity) : IAccountService
{
    public async Task<OperationResult> LoginAsync(LoginModel model)
    {
        var user = await users.FindByEmailAsync(model.Email.Trim());
        var result = user is null || !user.IsActive ? SignInResult.Failed
            : await signIn.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: true);
        var membershipAllowed = user?.Role != Roles.Member || (user.MemberId is int id
            && await members.MemberAsync(id, default) is { IsArchived: false });
        if (result.Succeeded && membershipAllowed && user is not null && (model.Portal is null || model.Portal == user.Role))
        {
            await signIn.SignInAsync(user, model.RememberMe);
            await activity.RecordActivityAsync(user.Id,"Signed in",default);
            logger.LogInformation("User {UserId} signed in with role {Role}", user.Id, user.Role);
            return OperationResult.Success;
        }
        return OperationResult.Failed("SignInFailed", "Sign-in failed. Check your credentials and selected role, or try again later.");
    }

    public async Task SignOutAsync()
    {
        var id=accessor.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if(id is not null)await activity.RecordActivityAsync(id,"Signed out",default);
        await signIn.SignOutAsync();
    }
    public async Task<OperationResult> ChangePasswordAsync(string userId, ChangePasswordModel model, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null) return OperationResult.NotFound;
        user.AuditActorId=user.Id;user.AuditAction="Password changed";
        var result = await users.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (result.Succeeded)
        {
            await sessions.RevokeAllAsync(user.Id, null, ct);
            await signIn.RefreshSignInAsync(user);
            logger.LogInformation("Password changed for user {UserId}", user.Id);
        }
        return result.ToOperationResult();
    }
    public async Task<Guid?> CurrentSessionIdAsync()
    {
        var context = accessor.HttpContext ?? throw new InvalidOperationException("No active HTTP request.");
        var result = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        return result.Properties?.Items.TryGetValue(LibraryCookieEvents.SessionKey, out var value) == true
            && Guid.TryParse(value, out var id) ? id : null;
    }
}
