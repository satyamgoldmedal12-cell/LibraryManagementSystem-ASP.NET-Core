using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Application.DTOs;

public sealed class LibrarySettings
{
    [Required, StringLength(120), Display(Name = "Library name")]
    public string LibraryName { get; set; } = "Library Desk";
    [StringLength(500)] public string? Address { get; set; }
    [EmailAddress, StringLength(256)] public string? Email { get; set; }
    [Phone, StringLength(30)] public string? Phone { get; set; }
    [Range(1, 100), Display(Name = "Maximum books per member")]
    public int MaxActiveLoans { get; set; } = 5;
    [Range(1, 90), Display(Name = "Default issue duration (days)")]
    public int DefaultIssueDays { get; set; } = 14;
    [Range(typeof(decimal), "0", "1000"), Display(Name = "Fine per day (₹)")]
    public decimal DailyFine { get; set; } = 5m;
    [Range(1, 120), Display(Name = "Membership duration (months)")]
    public int MembershipDurationMonths { get; set; } = 12;
    [Range(1, int.MaxValue)] public int Revision { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public string? ModifiedByName { get; set; }
}
