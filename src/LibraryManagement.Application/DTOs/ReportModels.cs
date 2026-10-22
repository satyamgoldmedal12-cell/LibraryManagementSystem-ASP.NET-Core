using System.ComponentModel.DataAnnotations;
namespace LibraryManagement.Application.DTOs;

public sealed class ReportFilter
{
    public string Report { get; set; } = "stock";
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    [Range(1,int.MaxValue)] public int? MemberId { get; set; }
    [Range(1,int.MaxValue)] public int? BookId { get; set; }
    public string Status { get; set; } = "all";
    [Range(1,100000)] public int Page { get; set; } = 1;
}
public sealed class ReportRow
{
    public int RecordId { get; set; }
    public string Title { get; set; } = "";
    public string ISBN { get; set; } = "";
    public string Author { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Category { get; set; } = "";
    public string MemberName { get; set; } = "";
    public string MemberNumber { get; set; } = "";
    public string StaffName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime? EventDate { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public int TotalCopies { get; set; }
    public int AvailableCopies { get; set; }
    public int IssuedCount { get; set; }
    public int ReturnedCount { get; set; }
    public int ActiveCount { get; set; }
    public int OverdueCount { get; set; }
    public int LateDays { get; set; }
    public int PaymentCount { get; set; }
    public decimal DailyFine { get; set; }
    public decimal FineAmount { get; set; }
    public decimal CollectedAmount { get; set; }
    public decimal PendingFine { get; set; }
}
public sealed class ReportTotals
{
    public int TotalRecords { get; set; }
    public long TotalCopies { get; set; }
    public long AvailableCopies { get; set; }
    public long IssuedCount { get; set; }
    public long ReturnedCount { get; set; }
    public long OverdueCount { get; set; }
    public decimal FineAmount { get; set; }
    public decimal CollectedAmount { get; set; }
    public DateTime GeneratedAt { get; set; }
}
public enum ReportValueType { Text, Number, Money, Date, Timestamp, Month }
public sealed record ReportColumn(string Heading,string Field,ReportValueType Type,int Width);
public sealed record ReportDefinition(string Key,string Title,string DateBasis,string Notes,IReadOnlyList<string> Statuses,IReadOnlyList<ReportColumn> Columns);
public sealed record ReportData(IReadOnlyList<ReportRow> Rows,ReportTotals Totals);
public sealed record ReportCell(string Text,ReportValueType Type,decimal? Number=null,DateTime? Date=null);
public sealed record ReportResult(ReportFilter Filter,ReportDefinition Definition,ReportTotals Totals,IReadOnlyList<IReadOnlyList<ReportCell>> Rows,int PageSize)
{
    public int TotalPages => Math.Max(1,(int)Math.Ceiling(Totals.TotalRecords/(double)PageSize));
}
public sealed record ReportFile(byte[] Content,string ContentType,string FileName);
