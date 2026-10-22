using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Application.DTOs;

public sealed class CirculationRecord
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public int MemberId { get; set; }
    public string Title { get; set; } = "";
    public string ISBN { get; set; } = "";
    public string MemberName { get; set; } = "";
    public string MemberNumber { get; set; } = "";
    public DateTime IssuedAt { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public string? ReturnedByName { get; set; }
    public int LateDays { get; set; }
    public decimal DailyFine { get; set; }
    public decimal FineAmount { get; set; }
    public int AvailableCopies { get; set; }
    public int? PaymentId { get; set; }
    public DateTime? PaidAt { get; set; }
    public decimal PaidAmount { get; set; }
    public string? ReceivedByName { get; set; }
    public DateTime AsOfUtc { get; set; }
    public string FineStatus => PaymentId.HasValue ? "Paid" : ReturnedAt is null ? "Accruing" : FineAmount > 0 ? "Pending" : "No fine";
}

public sealed class CirculationFilter
{
    [StringLength(200)] public string? Search { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    [Range(1, int.MaxValue)] public int? MemberId { get; set; }
    [Range(1, int.MaxValue)] public int? BookId { get; set; }
    public string Status { get; set; } = "all";
    [Range(1,100000)] public int Page { get; set; } = 1;
}

public sealed class FineTotals
{
    public int TotalRecords { get; set; }
    public decimal PendingFine { get; set; }
    public decimal AccruingFine { get; set; }
    public decimal PaidFine { get; set; }
}

public sealed record CirculationPage(PagedResult<CirculationRecord> Records, FineTotals Totals);

public sealed class FinePolicy
{
    [Range(typeof(decimal), "0", "1000")]
    public decimal DailyFine { get; set; }
    [Range(1, int.MaxValue)] public int Revision { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public string? ModifiedByName { get; set; }
}
