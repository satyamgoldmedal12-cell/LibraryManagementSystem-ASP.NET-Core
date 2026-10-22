using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LibraryManagement.Infrastructure.Identity;

// Three fixed roles. Only an administrator may assign a role when creating an account.
public sealed class LibraryClaimsFactory(UserManager<ApplicationUser> users, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(Options.ClaimsIdentity.RoleClaimType, user.Role));
        identity.AddClaim(new Claim("full_name", user.FullName));
        if (user.MemberId is int id) identity.AddClaim(new Claim("member_id", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return identity;
    }
}
