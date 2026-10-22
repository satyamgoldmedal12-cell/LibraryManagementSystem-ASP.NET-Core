using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Application.DTOs;

public sealed class LendingOptions
{
    [Range(1, 100)] public int MaxActiveLoans { get; set; } = 5;
    [Range(typeof(decimal), "0", "1000")] public decimal DailyFine { get; set; } = 5m;
}
