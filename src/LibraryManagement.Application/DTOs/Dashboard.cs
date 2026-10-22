using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Application.DTOs;

public sealed class Dashboard
{
    public long TotalBooks { get; set; }
    public long AvailableBooks { get; set; }
    public long IssuedBooks { get; set; }
    public long ReturnedBooks { get; set; }
    public long OverdueBooks { get; set; }
    public long TotalMembers { get; set; }
    public decimal TotalFineCollected { get; set; }
    public long BookTitles { get; set; }
    public long ActiveMembers { get; set; }
    public DateTime RefreshedAt { get; set; }
    public IReadOnlyList<MonthlyCirculation> MonthlyActivity { get; set; } = [];
}
