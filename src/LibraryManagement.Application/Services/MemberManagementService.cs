namespace LibraryManagement.Application.Services;

public sealed class MemberManagementService(ILibraryRepository repository, TimeProvider clock) : IMemberManagementService
{
    public Task<PagedResult<Member>> ListAsync(string? search, int page, bool includeArchived, string status, CancellationToken ct) =>
        repository.MembersPageAsync(search, Math.Clamp(page, 1, 100000), includeArchived, status is "active" or "inactive" or "expired" ? status : "all", ct);

    public Task<Member?> GetAsync(int id, CancellationToken ct) => repository.MemberAsync(id, ct);

    public async Task<MemberProfile?> ProfileAsync(int id, int page, CancellationToken ct)
    {
        var member = await repository.MemberAsync(id, ct);
        if (member is null) return null;

        var stats = await repository.MemberStatisticsAsync(id, ct);
        var history = await repository.LoansAsync(null, "all", id, Math.Clamp(page, 1, 100000), ct);
        return new(member, stats, history);
    }

    public Task<int> SaveAsync(Member member, string actor, CancellationToken ct)
    {
        MemberValidation.NormalizeAndValidate(member, actor, LibraryManagement.Domain.Rules.IndiaTime.FromUtc(clock.GetUtcNow()).Date);
        return repository.SaveMemberAsync(member, actor, ct);
    }

    private static void ValidateAction(int id, int revision, string actor)
    {
        if (id < 1 || revision < 1 || string.IsNullOrWhiteSpace(actor)) throw new BusinessException("Reload the member and try again.");
    }

    public Task DeleteAsync(int id, int revision, string actor, CancellationToken ct)
    {
        ValidateAction(id, revision, actor);
        return repository.DeleteMemberAsync(id, revision, actor, ct);
    }

    public Task RestoreAsync(int id, int revision, string actor, CancellationToken ct)
    {
        ValidateAction(id, revision, actor);
        return repository.RestoreMemberAsync(id, revision, actor, ct);
    }
}
