using System.Data;
using Dapper;
namespace LibraryManagement.Infrastructure.Repositories;
public sealed class ReportRepository(SqlConnectionFactory factory) : IReportRepository
{
    public async Task<ReportData> QueryAsync(ReportFilter filter,int pageSize,CancellationToken ct)
    {
        await using var connection=await factory.OpenAsync(ct);
        using var grid=await connection.QueryMultipleAsync(new CommandDefinition("library.Reports_Query",new{filter.Report,filter.FromDate,filter.ToDate,filter.MemberId,filter.BookId,filter.Status,filter.Page,PageSize=pageSize},commandType:CommandType.StoredProcedure,commandTimeout:60,cancellationToken:ct));
        var rows=(await grid.ReadAsync<ReportRow>()).AsList();return new(rows,await grid.ReadSingleAsync<ReportTotals>());
    }
}
