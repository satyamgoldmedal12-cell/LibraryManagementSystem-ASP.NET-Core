namespace LibraryManagement.Application.Services;

public sealed class UserSessionService(IUserSessionRepository repository, AuthenticationSessionOptions options) : IUserSessionService
{
    public Task<IReadOnlyList<UserSession>> ListAsync(string userId, CancellationToken ct) => repository.ListAsync(userId, options.IdleMinutes, ct);

    public Task RevokeAsync(Guid id, string userId, CancellationToken ct) => repository.RevokeAsync(id, userId, ct);

    public Task RevokeOthersAsync(string userId, Guid currentId, CancellationToken ct) => repository.RevokeAllAsync(userId, currentId, ct);
}
