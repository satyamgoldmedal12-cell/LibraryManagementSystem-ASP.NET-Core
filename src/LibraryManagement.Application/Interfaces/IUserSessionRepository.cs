namespace LibraryManagement.Application.Interfaces;

public interface IUserSessionRepository
{
    Task CreateAsync(Guid id, string userId, string device, int absoluteHours, CancellationToken ct);
    Task<bool> ValidateAsync(Guid id, string userId, int idleMinutes, CancellationToken ct);
    Task RevokeAsync(Guid id, string userId, CancellationToken ct);
    Task RevokeAllAsync(string userId, Guid? exceptId, CancellationToken ct);
    Task<IReadOnlyList<UserSession>> ListAsync(string userId, int idleMinutes, CancellationToken ct);
}
