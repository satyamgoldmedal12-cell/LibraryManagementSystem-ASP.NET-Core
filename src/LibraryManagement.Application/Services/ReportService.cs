using System.Globalization;
namespace LibraryManagement.Application.Services;

public sealed class ReportService(IReportRepository repository,IReportExporter exporter,TimeProvider clock) : IReportService
{
    public const int ExportLimit = 5000;
    public async Task<ReportResult> GetAsync(ReportFilter filter,bool export,CancellationToken ct)
    {
        var definition=ReportCatalog.Find(filter.Report);
        if(!definition.Statuses.Contains(filter.Status)) throw new BusinessException("Choose a status supported by this report.");
        if(filter.Page is <1 or >100000 || filter.BookId is <=0 || filter.MemberId is <=0) throw new BusinessException("Check the page, book and member filters.");
        filter.FromDate=filter.FromDate?.Date;filter.ToDate=filter.ToDate?.Date;
        if(filter.Report=="monthly")
        {
            filter.ToDate??=LibraryManagement.Domain.Rules.IndiaTime.FromUtc(clock.GetUtcNow()).Date;
            filter.FromDate??=new DateTime(filter.ToDate.Value.Year,filter.ToDate.Value.Month,1).AddMonths(-11);
        }
        if(filter.FromDate>filter.ToDate) throw new BusinessException("From date cannot be after To date.");
        foreach(var date in new[]{filter.FromDate,filter.ToDate})
            if(date.HasValue&&(date.Value.Year<1900||date.Value.Year>9998)) throw new BusinessException("Choose dates between 1900 and 9998.");
        if(filter.Report=="monthly" && (filter.ToDate!.Value.Year-filter.FromDate!.Value.Year)*12+filter.ToDate.Value.Month-filter.FromDate.Value.Month>119)
            throw new BusinessException("Monthly activity can include up to 120 months. Narrow the dates.");
        if(export)filter.Page=1;
        var pageSize=export?ExportLimit+1:20;
        var data=await repository.QueryAsync(filter,pageSize,ct);
        if(export && data.Totals.TotalRecords>ExportLimit) throw new BusinessException("Export supports up to 5,000 rows. Narrow the filters and try again; no rows were exported.");
        var rows=data.Rows.Select(row=>(IReadOnlyList<ReportCell>)definition.Columns.Select(column=>Cell(row,column)).ToArray()).ToArray();
        return new(filter,definition,data.Totals,rows,pageSize);
    }
    public async Task<ReportFile> ExportAsync(ReportFilter filter,string format,CancellationToken ct)
    {
        if(format is not ("excel" or "pdf")) throw new BusinessException("Choose Excel or PDF export.");
        return exporter.Export(await GetAsync(filter,true,ct),format,ct);
    }
    private static ReportCell Cell(ReportRow row,ReportColumn column)
    {
        var value=typeof(ReportRow).GetProperty(column.Field)!.GetValue(row);
        if(value is null)return new("",column.Type);
        if(value is DateTime date)return new(date.ToString(column.Type==ReportValueType.Month?"MMM yyyy":column.Type==ReportValueType.Timestamp?"yyyy-MM-dd HH:mm:ss":"yyyy-MM-dd",CultureInfo.InvariantCulture),column.Type,Date:date);
        if(column.Type is ReportValueType.Number or ReportValueType.Money)
        {
            var number=Convert.ToDecimal(value,CultureInfo.InvariantCulture);
            return new(number.ToString(column.Type==ReportValueType.Money?"N2":"0",CultureInfo.InvariantCulture),column.Type,number);
        }
        return new(Convert.ToString(value,CultureInfo.InvariantCulture)??"",column.Type);
    }
}
