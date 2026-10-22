using System.Data;
using Dapper;
using Microsoft.AspNetCore.Identity;

namespace LibraryManagement.Infrastructure.Identity;

public sealed class UserAccountIdentity(UserManager<ApplicationUser> users, SqlConnectionFactory factory,
    IUserSessionRepository sessions, ILogger<UserAccountIdentity> logger) : IUserAccountIdentity
{
    public async Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        return (await connection.QueryAsync<UserSummary>(new CommandDefinition("library.Users_List",
            commandType: CommandType.StoredProcedure, cancellationToken: ct, commandTimeout: 30))).AsList();
    }
    public Task<OperationResult> CreateAsync(CreateUserModel model) => CreateCoreAsync(model,null);
    public Task<OperationResult> CreateAsync(CreateUserModel model,string actor,CancellationToken ct) => CreateCoreAsync(model,actor);
    private async Task<OperationResult> CreateCoreAsync(CreateUserModel model,string? actor)
    {
        var user = new ApplicationUser
        {
            FullName = model.FullName.Trim(),
            UserName = model.Email.Trim(),
            Email = model.Email.Trim(),
            Role = model.Role,
            MemberId = model.MemberId,
            AuditActorId = actor,
            LockoutEnabled = true
        };
        var result = await users.CreateAsync(user, model.Password);
        if (result.Succeeded) logger.LogInformation("Created user {UserId} with role {Role}", user.Id, user.Role);
        return result.ToOperationResult();
    }
    public Task<OperationResult> ResetPasswordAsync(string userId,string password,CancellationToken ct) => ResetCoreAsync(userId,password,null,ct);
    public Task<OperationResult> ResetPasswordAsync(string userId,string password,string actor,CancellationToken ct) => ResetCoreAsync(userId,password,actor,ct);
    private async Task<OperationResult> ResetCoreAsync(string userId,string password,string? actor,CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null) return OperationResult.NotFound;
        user.AuditActorId=actor; user.AuditAction=actor is null?"Password reset":"Password reset by admin";
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, password);
        if (result.Succeeded)
        {
            await sessions.RevokeAllAsync(user.Id, null, ct);
            logger.LogInformation("Administrator reset password for user {UserId}", user.Id);
        }
        return result.ToOperationResult();
    }
    public Task<OperationResult> SetAccessAsync(string userId,bool enabled,CancellationToken ct) => AccessCoreAsync(userId,enabled,null,ct);
    public Task<OperationResult> SetAccessAsync(string userId,bool enabled,string actor,CancellationToken ct) => AccessCoreAsync(userId,enabled,actor,ct);
    private async Task<OperationResult> AccessCoreAsync(string userId,bool enabled,string? actor,CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null) return OperationResult.NotFound;
        user.IsActive=enabled;user.AuditActorId=actor;user.AuditAction=enabled?"User enabled":"User disabled";
        // Legacy trusted callers use the target as actor only for non-administrative recovery.
        user.LockoutEnd = enabled ? null : DateTimeOffset.MaxValue;
        user.AccessFailedCount = 0;
        user.SecurityStamp = Guid.NewGuid().ToString();
        var result = await users.UpdateAsync(user);
        if (result.Succeeded)
        {
            await sessions.RevokeAllAsync(user.Id, null, ct);
            logger.LogInformation("Set access {Enabled} for user {UserId}", enabled, user.Id);
        }
        return result.ToOperationResult();
    }

    private static CommandDefinition Command(string name,object args,CancellationToken ct) => new("library."+name,args,commandType:CommandType.StoredProcedure,commandTimeout:30,cancellationToken:ct);
    public async Task<UserSummary?> SummaryAsync(string id,CancellationToken ct)
    {
        await using var connection=await factory.OpenAsync(ct);return await connection.QuerySingleOrDefaultAsync<UserSummary>(Command("Users_Summary",new{Id=id},ct));
    }
    public async Task<PagedResult<UserSummary>> PageAsync(UserFilter filter,CancellationToken ct)
    {
        await using var connection=await factory.OpenAsync(ct);using var grid=await connection.QueryMultipleAsync(Command("Users_Page",new{filter.Search,filter.Role,filter.Status,filter.Page},ct));
        var rows=(await grid.ReadAsync<UserSummary>()).AsList();return new(rows,await grid.ReadSingleAsync<int>(),filter.Page,20,filter.Search,filter.Status);
    }
    public async Task<OperationResult> EditAsync(EditUserModel model,string actor,CancellationToken ct)
    {
        var user=await users.FindByIdAsync(model.Id);if(user is null)return OperationResult.NotFound;
        if(user.Revision!=model.Revision)return OperationResult.Failed("Concurrency","This account changed. Reload before saving again.");
        if(!string.Equals(user.Email,model.Email,StringComparison.OrdinalIgnoreCase))user.EmailConfirmed=false;
        user.FullName=model.FullName;user.Email=model.Email;user.UserName=model.Email;user.Role=model.Role;user.MemberId=model.MemberId;
        user.SecurityStamp=Guid.NewGuid().ToString();user.AuditActorId=actor;user.AuditAction="User edited";user.ExpectedRevision=model.Revision;
        return (await users.UpdateAsync(user)).ToOperationResult();
    }
    public async Task<PagedResult<UserActivity>> ActivityAsync(UserActivityFilter filter,CancellationToken ct)
    {
        await using var connection=await factory.OpenAsync(ct);using var grid=await connection.QueryMultipleAsync(Command("UserActivity_Page",new{filter.UserId,filter.Action,filter.FromDate,filter.ToDate,filter.Page},ct));
        var rows=(await grid.ReadAsync<UserActivity>()).AsList();return new(rows,await grid.ReadSingleAsync<int>(),filter.Page,20);
    }
    public async Task RecordActivityAsync(string actor,string action,CancellationToken ct)
    {
        await using var connection=await factory.OpenAsync(ct);await connection.ExecuteAsync(Command("UserActivity_Add",new{ActorId=actor,Action=action},ct));
    }
}
