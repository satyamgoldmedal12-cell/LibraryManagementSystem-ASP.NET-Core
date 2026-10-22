namespace LibraryManagement.Application.Interfaces;
public interface IReportService
{
    Task<ReportResult> GetAsync(ReportFilter filter,bool export,CancellationToken ct);
    Task<ReportFile> ExportAsync(ReportFilter filter,string format,CancellationToken ct);
}
public interface IReportRepository { Task<ReportData> QueryAsync(ReportFilter filter,int pageSize,CancellationToken ct); }
public interface IReportExporter { ReportFile Export(ReportResult result,string format,CancellationToken ct); }
