using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Application.DTOs;

public sealed class IssueLoan
{
    [Range(1, int.MaxValue, ErrorMessage = "Select a book from the search results.")]
    public int BookId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Select a member from the search results.")]
    public int MemberId { get; set; }
    [DataType(DataType.Date), Display(Name = "Due date")]
    public DateTime DueDate { get; set; } = LibraryManagement.Domain.Rules.IndiaTime.Today.AddDays(14);
}
