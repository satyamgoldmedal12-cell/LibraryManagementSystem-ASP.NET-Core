using Microsoft.AspNetCore.Identity;

namespace LibraryManagement.Infrastructure.Identity;

public sealed class MemberAccountProvisioner(UserManager<ApplicationUser> users) : IMemberAccountProvisioner
{
    public async Task<OperationResult> CreateAsync(string fullName, string email, string password)
    {
        var user = new ApplicationUser
        {
            FullName = fullName,
            Email = email,
            UserName = email,
            Role = Roles.Member,
            LockoutEnabled = true,
            CreateMembershipOnRegistration = true
        };
        // Identity validates and hashes; the Dapper store atomically creates account and membership.
        return (await users.CreateAsync(user, password)).ToOperationResult();
    }
}
