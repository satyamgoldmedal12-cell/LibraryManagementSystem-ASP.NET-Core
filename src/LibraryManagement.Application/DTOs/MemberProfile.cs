namespace LibraryManagement.Application.DTOs;

public sealed class MemberStatistics
{
    public int ActiveLoans { get; set; }
    public int ReturnedLoans { get; set; }
    public int OverdueLoans { get; set; }
    public decimal UnpaidFines { get; set; }
    public decimal PaidFines { get; set; }
}
public sealed record MemberProfile(Member Member, MemberStatistics Statistics, PagedResult<Loan> History);
