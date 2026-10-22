namespace LibraryManagement.Application.Interfaces;

public interface IMemberManagementService
{
    Task<PagedResult<Member>> ListAsync(string? search, int page, bool includeArchived, string status, CancellationToken ct);
    Task<Member?> GetAsync(int id, CancellationToken ct);
    Task<MemberProfile?> ProfileAsync(int id, int page, CancellationToken ct);
    Task<int> SaveAsync(Member member, string actor, CancellationToken ct);
    Task DeleteAsync(int id, int revision, string actor, CancellationToken ct);
    Task RestoreAsync(int id, int revision, string actor, CancellationToken ct);
}
