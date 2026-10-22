namespace LibraryManagement.Application.Interfaces;

public interface IMemberRegistrationService
{
    Task<OperationResult> RegisterAsync(RegisterModel model);
}
public interface IMemberAccountProvisioner
{
    Task<OperationResult> CreateAsync(string fullName, string email, string password);
}
public interface IPasswordRecoveryService
{
    Task SendResetAsync(string email, CancellationToken ct);
    Task<OperationResult> ResetAsync(PublicResetPasswordModel model, CancellationToken ct);
}
public interface IPasswordResetEmailSender
{
    Task SendAsync(string recipient, string link, CancellationToken ct);
}
public interface IAccountService
{
    Task<OperationResult> LoginAsync(LoginModel model);
    Task SignOutAsync();
    Task<OperationResult> ChangePasswordAsync(string userId, ChangePasswordModel model, CancellationToken ct);
    Task<Guid?> CurrentSessionIdAsync();
}
public interface IUserAccountIdentity
{
    Task<PagedResult<UserSummary>> PageAsync(UserFilter filter,CancellationToken ct);
    Task<UserSummary?> SummaryAsync(string id,CancellationToken ct);
    Task<OperationResult> EditAsync(EditUserModel model,string actor,CancellationToken ct);
    Task<PagedResult<UserActivity>> ActivityAsync(UserActivityFilter filter,CancellationToken ct);
    Task<OperationResult> CreateAsync(CreateUserModel model,string actor,CancellationToken ct);
    Task<OperationResult> ResetPasswordAsync(string userId,string password,string actor,CancellationToken ct);
    Task<OperationResult> SetAccessAsync(string userId,bool enabled,string actor,CancellationToken ct);
    Task RecordActivityAsync(string actor,string action,CancellationToken ct);

    Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken ct);
    Task<OperationResult> CreateAsync(CreateUserModel model);
    Task<OperationResult> ResetPasswordAsync(string userId, string password, CancellationToken ct);
    Task<OperationResult> SetAccessAsync(string userId, bool enabled, CancellationToken ct);
}
public interface IUserAdministrationService
{
    Task<PagedResult<UserSummary>> PageAsync(UserFilter filter,CancellationToken ct);
    Task<UserSummary?> SummaryAsync(string id,CancellationToken ct);
    Task<OperationResult> EditAsync(EditUserModel model,string actor,CancellationToken ct);
    Task<PagedResult<UserActivity>> ActivityAsync(UserActivityFilter filter,CancellationToken ct);
    Task<OperationResult> CreateAsync(CreateUserModel model,string actor,CancellationToken ct);
    Task<OperationResult> ResetPasswordAsync(ResetPasswordModel model,string actor,CancellationToken ct);

    Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken ct);
    Task<OperationResult> CreateAsync(CreateUserModel model, CancellationToken ct = default);
    Task<OperationResult> ResetPasswordAsync(ResetPasswordModel model, CancellationToken ct);
    Task<OperationResult> SetAccessAsync(string userId, bool enabled, string actorId, CancellationToken ct);
}
public interface IUserSessionService
{
    Task<IReadOnlyList<UserSession>> ListAsync(string userId, CancellationToken ct);
    Task RevokeAsync(Guid id, string userId, CancellationToken ct);
    Task RevokeOthersAsync(string userId, Guid currentId, CancellationToken ct);
}
