namespace LibraryManagement.Application.Services;

public sealed class CirculationService(ICirculationRepository repository, ILibraryService commands, LendingOptions defaults) : ICirculationService
{
    public Task<CirculationRecord?> DetailsAsync(int id, CancellationToken ct) => repository.DetailsAsync(id, ct);

    public Task<CirculationPage> ListAsync(string kind, CirculationFilter filter, CancellationToken ct)
    {
        if (kind is not ("returns" or "fines" or "collections")) throw new BusinessException("Unknown circulation report.");
        filter.Search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim();
        filter.FromDate = filter.FromDate?.Date;
        filter.ToDate = filter.ToDate?.Date;
        if (filter.Search?.Length > 200 || filter.Page is < 1 or > 100000 || filter.BookId is <= 0 || filter.MemberId is <= 0)
            throw new BusinessException("Check the search, page and selected book or member.");
        if (filter.FromDate > filter.ToDate) throw new BusinessException("From date cannot be after To date.");
        if (filter.FromDate < new DateTime(1753,1,1) || filter.ToDate < new DateTime(1753,1,1)) throw new BusinessException("Choose a date from 1753 onwards.");
        if (filter.Status is not ("all" or "pending" or "paid" or "accruing")) throw new BusinessException("Choose a valid fine status.");
        return repository.ListAsync(kind, filter, ct);
    }

    public Task ReturnAsync(int id, string actor, CancellationToken ct)
    {
        EnsureCommand(id, actor);
        return commands.ReturnAsync(id, actor, ct);
    }

    public Task PayFineAsync(int id, string actor, CancellationToken ct)
    {
        EnsureCommand(id, actor);
        return commands.PayFineAsync(id, actor, ct);
    }

    public Task<FinePolicy> PolicyAsync(CancellationToken ct) => repository.PolicyAsync(defaults.DailyFine, ct);

    public Task SavePolicyAsync(FinePolicy policy, string actor, CancellationToken ct)
    {
        EnsureCommand(policy.Revision, actor);
        FineRules.EnsureRate(policy.DailyFine);
        return repository.SavePolicyAsync(policy, actor, ct);
    }

    private static void EnsureCommand(int id, string actor)
    {
        if (id < 1 || string.IsNullOrWhiteSpace(actor)) throw new BusinessException("A valid record and signed-in staff account are required.");
    }
}
