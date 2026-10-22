using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Infrastructure.Repositories;

public sealed class CirculationRepository(SqlConnectionFactory factory) : ICirculationRepository
{
    private static CommandDefinition Command(string procedure, object? args, CancellationToken ct) =>
        new("library." + procedure, args, commandType: CommandType.StoredProcedure, commandTimeout: 30, cancellationToken: ct);

    public async Task<CirculationRecord?> DetailsAsync(int id, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<CirculationRecord>(Command("Circulation_Get", new { Id = id }, ct));
    }

    public async Task<CirculationPage> ListAsync(string kind, CirculationFilter filter, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        using var grid = await connection.QueryMultipleAsync(Command("Circulation_List", new
        {
            Kind = kind, filter.Search, filter.FromDate, filter.ToDate, filter.MemberId, filter.BookId, filter.Status, filter.Page
        }, ct));
        var rows = (await grid.ReadAsync<CirculationRecord>()).AsList();
        var totals = await grid.ReadSingleAsync<FineTotals>();
        return new(new(rows, totals.TotalRecords, filter.Page, 12, filter.Search, filter.Status), totals);
    }

    public async Task<FinePolicy> PolicyAsync(decimal defaultFine, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        return await connection.QuerySingleAsync<FinePolicy>(Command("FinePolicy_Get", new { DefaultFine = defaultFine }, ct));
    }

    public async Task SavePolicyAsync(FinePolicy policy, string actor, CancellationToken ct)
    {
        try
        {
            await using var connection = await factory.OpenAsync(ct);
            await connection.ExecuteAsync(Command("FinePolicy_Save", new { policy.DailyFine, policy.Revision, ActorId = actor }, ct));
        }
        catch (SqlException ex) when (ex.Number == 50001) { throw new BusinessException(ex.Message); }
        catch (SqlException ex) when (ex.Number == 1205) { throw new BusinessException("Another operation was running. Reload and try again."); }
    }
}
