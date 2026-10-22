using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Application.Interfaces;

public interface IPagedResult
{
    int Page { get; }
    int Total { get; }
    int TotalPages { get; }
    string? Search { get; }
    string? Status { get; }
    bool IncludeArchived { get; }
}
