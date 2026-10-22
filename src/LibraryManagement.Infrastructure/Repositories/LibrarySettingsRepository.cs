using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Infrastructure.Repositories;

public sealed class LibrarySettingsRepository(SqlConnectionFactory factory) : ILibrarySettingsRepository
{
    private static CommandDefinition Command(string name, object args, CancellationToken ct) =>
        new("library." + name, args, commandType: CommandType.StoredProcedure, commandTimeout: 30, cancellationToken: ct);

    public async Task<LibrarySettings> GetAsync(LendingOptions defaults, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        return await connection.QuerySingleAsync<LibrarySettings>(Command("Settings_Get",
            new { DefaultMaxLoans = defaults.MaxActiveLoans, DefaultFine = defaults.DailyFine }, ct));
    }

    public async Task SaveAsync(LibrarySettings settings, string actor, CancellationToken ct)
    {
        try
        {
            await using var connection = await factory.OpenAsync(ct);
            await connection.ExecuteAsync(Command("Settings_Save", new
            {
                settings.LibraryName, settings.Address, settings.Email, settings.Phone,
                settings.MaxActiveLoans, settings.DefaultIssueDays, settings.DailyFine,
                settings.MembershipDurationMonths, settings.Revision, ActorId = actor
            }, ct));
        }
        catch (SqlException ex) when (ex.Number == 50001) { throw new BusinessException(ex.Message); }
        catch (SqlException ex) when (ex.Number == 1205) { throw new BusinessException("Another operation was running. Reload settings and try again."); }
    }
}
