using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Application.DTOs;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize,
    string? Search = null, string? Status = null, bool IncludeArchived = false) : IPagedResult
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}
