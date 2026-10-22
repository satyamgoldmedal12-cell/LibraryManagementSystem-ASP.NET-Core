using System.Data;
using Dapper;

namespace LibraryManagement.Infrastructure.Repositories;

public sealed class UserSessionRepository(SqlConnectionFactory factory) : IUserSessionRepository
{
    private static CommandDefinition Command(string name, object args, CancellationToken ct)
    {
        return new CommandDefinition(
            "library.Sessions_" + name,
            args,
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct,
            commandTimeout: 30);
    }

    public async Task CreateAsync(Guid id, string userId, string device, int absoluteHours, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await connection.ExecuteAsync(Command("Create", new { Id = id, UserId = userId, Device = device, AbsoluteHours = absoluteHours }, ct));
    }

    public async Task<bool> ValidateAsync(Guid id, string userId, int idleMinutes, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(Command("Validate", new { Id = id, UserId = userId, IdleMinutes = idleMinutes }, ct)) == 1;
    }

    public async Task RevokeAsync(Guid id, string userId, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await connection.ExecuteAsync(Command("Revoke", new { Id = id, UserId = userId }, ct));
    }

    public async Task RevokeAllAsync(string userId, Guid? exceptId, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await connection.ExecuteAsync(Command("RevokeAll", new { UserId = userId, ExceptId = exceptId }, ct));
    }

    public async Task<IReadOnlyList<UserSession>> ListAsync(string userId, int idleMinutes, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        return (await connection.QueryAsync<UserSession>(Command("List", new { UserId = userId, IdleMinutes = idleMinutes }, ct))).AsList();
    }
}
