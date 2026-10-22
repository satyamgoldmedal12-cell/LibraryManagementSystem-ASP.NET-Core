namespace LibraryManagement.Application.Interfaces;

public interface ICirculationService
{
    Task<CirculationRecord?> DetailsAsync(int id, CancellationToken ct);
    Task<CirculationPage> ListAsync(string kind, CirculationFilter filter, CancellationToken ct);
    Task ReturnAsync(int id, string actor, CancellationToken ct);
    Task PayFineAsync(int id, string actor, CancellationToken ct);
    Task<FinePolicy> PolicyAsync(CancellationToken ct);
    Task SavePolicyAsync(FinePolicy policy, string actor, CancellationToken ct);
}

public interface ICirculationRepository
{
    Task<CirculationRecord?> DetailsAsync(int id, CancellationToken ct);
    Task<CirculationPage> ListAsync(string kind, CirculationFilter filter, CancellationToken ct);
    Task<FinePolicy> PolicyAsync(decimal defaultFine, CancellationToken ct);
    Task SavePolicyAsync(FinePolicy policy, string actor, CancellationToken ct);
}
