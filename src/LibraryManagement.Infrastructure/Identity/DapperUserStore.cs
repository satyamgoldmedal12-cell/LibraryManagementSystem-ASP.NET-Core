using System.Data;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Infrastructure.Identity;

// Identity owns password hashing, validation, cookies and lockout. This class only persists users.
public sealed class DapperUserStore(SqlConnectionFactory factory, IdentityErrorDescriber errors) :
    IUserPasswordStore<ApplicationUser>, IUserEmailStore<ApplicationUser>,
    IUserSecurityStampStore<ApplicationUser>, IUserLockoutStore<ApplicationUser>
{
    private static CommandDefinition Command(string name, object args, CancellationToken ct) =>
        new("library.Users_" + name, args, commandType: CommandType.StoredProcedure, cancellationToken: ct, commandTimeout: 30);
    private static DynamicParameters Parameters(ApplicationUser u) => new(new
    {
        u.Id,
        u.UserName,
        u.NormalizedUserName,
        u.Email,
        u.NormalizedEmail,
        u.EmailConfirmed,
        u.PasswordHash,
        u.SecurityStamp,
        u.ConcurrencyStamp,
        u.LockoutEnd,
        u.LockoutEnabled,
        u.AccessFailedCount,
        u.FullName,
        u.Role,
        u.MemberId,
        u.IsActive,
        ActorId = u.AuditActorId,
        AuditAction = u.AuditAction
    });
    public async Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        try
        {
            if (user.CreateMembershipOnRegistration)
            {
                if (user.Role != Roles.Member || user.MemberId is not null)
                    return IdentityResult.Failed(new IdentityError { Code = "InvalidRegistration", Description = "Registration can only create a new member account." });
                user.MemberId = await connection.QuerySingleAsync<int>(Command("RegisterMember", new
                {
                    user.Id,
                    user.UserName,
                    user.NormalizedUserName,
                    user.Email,
                    user.NormalizedEmail,
                    user.PasswordHash,
                    user.SecurityStamp,
                    user.ConcurrencyStamp,
                    user.FullName
                }, ct));
                user.CreateMembershipOnRegistration = false;
            }
            else await connection.ExecuteAsync(Command("Create", Parameters(user), ct));
            return IdentityResult.Success;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        { return IdentityResult.Failed(new IdentityError { Code = "DuplicateUser", Description = "This email or linked member is already registered. Sign in, or contact your library administrator." }); }
        catch (SqlException ex) when (ex.Number == 50001)
        { return IdentityResult.Failed(new IdentityError { Code="AccountRule",Description=ex.Message }); }
        catch (SqlException ex) when (ex.Number == 1205)
        { return IdentityResult.Failed(new IdentityError { Code = "RegistrationConflict", Description = "Another request was running. Please try again." }); }
    }
    public async Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken ct)
    {
        var previous = user.ConcurrencyStamp; var next = Guid.NewGuid().ToString();
        var args = Parameters(user); args.Add("PreviousStamp", previous); args.Add("ExpectedRevision", user.ExpectedRevision); args.Add("ConcurrencyStamp", next);
        await using var connection = await factory.OpenAsync(ct);
        int rows;
        try { rows = await connection.ExecuteScalarAsync<int>(Command("Update", args, ct)); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { return IdentityResult.Failed(new IdentityError { Code="DuplicateUser",Description="This email or membership is already linked to another account." }); }
        catch (SqlException ex) when (ex.Number == 50001) { return IdentityResult.Failed(new IdentityError { Code="AccountRule",Description=ex.Message }); }
        catch (SqlException ex) when (ex.Number == 1205) { return IdentityResult.Failed(errors.ConcurrencyFailure()); }
        if (rows != 1) return IdentityResult.Failed(errors.ConcurrencyFailure());
        user.ConcurrencyStamp = next;
        return IdentityResult.Success;
    }
    public async Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        var rows = await connection.ExecuteScalarAsync<int>(Command("Delete", new { user.Id, user.ConcurrencyStamp }, ct));
        return rows == 1 ? IdentityResult.Success : IdentityResult.Failed(errors.ConcurrencyFailure());
    }
    private async Task<ApplicationUser?> FindAsync(object args, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<ApplicationUser>(Command("Find", args, ct));
    }
    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken ct) => FindAsync(new { Id = userId }, ct);
    public Task<ApplicationUser?> FindByNameAsync(string name, CancellationToken ct) => FindAsync(new { NormalizedUserName = name }, ct);
    public Task<ApplicationUser?> FindByEmailAsync(string email, CancellationToken ct) => FindAsync(new { NormalizedEmail = email }, ct);
    private static Task Done(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    private static Task<T> Value<T>(T value, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(value); }
    public Task<string> GetUserIdAsync(ApplicationUser u, CancellationToken ct) => Value(u.Id, ct);
    public Task<string?> GetUserNameAsync(ApplicationUser u, CancellationToken ct) => Value(u.UserName, ct);
    public Task SetUserNameAsync(ApplicationUser u, string? value, CancellationToken ct) { u.UserName = value; return Done(ct); }
    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser u, CancellationToken ct) => Value(u.NormalizedUserName, ct);
    public Task SetNormalizedUserNameAsync(ApplicationUser u, string? value, CancellationToken ct) { u.NormalizedUserName = value; return Done(ct); }
    public Task SetPasswordHashAsync(ApplicationUser u, string? hash, CancellationToken ct) { u.PasswordHash = hash; return Done(ct); }
    public Task<string?> GetPasswordHashAsync(ApplicationUser u, CancellationToken ct) => Value(u.PasswordHash, ct);
    public Task<bool> HasPasswordAsync(ApplicationUser u, CancellationToken ct) => Value(u.PasswordHash is not null, ct);
    public Task SetEmailAsync(ApplicationUser u, string? email, CancellationToken ct) { u.Email = email; return Done(ct); }
    public Task<string?> GetEmailAsync(ApplicationUser u, CancellationToken ct) => Value(u.Email, ct);
    public Task<bool> GetEmailConfirmedAsync(ApplicationUser u, CancellationToken ct) => Value(u.EmailConfirmed, ct);
    public Task SetEmailConfirmedAsync(ApplicationUser u, bool confirmed, CancellationToken ct) { u.EmailConfirmed = confirmed; return Done(ct); }
    public Task<string?> GetNormalizedEmailAsync(ApplicationUser u, CancellationToken ct) => Value(u.NormalizedEmail, ct);
    public Task SetNormalizedEmailAsync(ApplicationUser u, string? email, CancellationToken ct) { u.NormalizedEmail = email; return Done(ct); }
    public Task SetSecurityStampAsync(ApplicationUser u, string stamp, CancellationToken ct) { u.SecurityStamp = stamp; return Done(ct); }
    public Task<string?> GetSecurityStampAsync(ApplicationUser u, CancellationToken ct) => Value(u.SecurityStamp, ct);
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser u, CancellationToken ct) => Value(u.LockoutEnd, ct);
    public Task SetLockoutEndDateAsync(ApplicationUser u, DateTimeOffset? end, CancellationToken ct) { u.LockoutEnd = end; return Done(ct); }
    public Task<int> IncrementAccessFailedCountAsync(ApplicationUser u, CancellationToken ct) => Value(++u.AccessFailedCount, ct);
    public Task ResetAccessFailedCountAsync(ApplicationUser u, CancellationToken ct) { u.AccessFailedCount = 0; return Done(ct); }
    public Task<int> GetAccessFailedCountAsync(ApplicationUser u, CancellationToken ct) => Value(u.AccessFailedCount, ct);
    public Task<bool> GetLockoutEnabledAsync(ApplicationUser u, CancellationToken ct) => Value(u.LockoutEnabled, ct);
    public Task SetLockoutEnabledAsync(ApplicationUser u, bool enabled, CancellationToken ct) { u.LockoutEnabled = enabled; return Done(ct); }
    public void Dispose() { }
}
