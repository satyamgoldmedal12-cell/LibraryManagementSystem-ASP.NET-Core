using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Application.DTOs;

public sealed class MonthlyCirculation
{
    public DateTime Month { get; set; }
    public long IssuedBooks { get; set; }
    public long ReturnedBooks { get; set; }
    public string Label => Month.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
    public string ShortLabel => Month.ToString("MMM", System.Globalization.CultureInfo.InvariantCulture);
}
