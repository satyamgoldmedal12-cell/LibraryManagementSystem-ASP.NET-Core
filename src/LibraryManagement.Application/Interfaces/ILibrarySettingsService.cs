namespace LibraryManagement.Application.Interfaces;

public interface ILibrarySettingsService
{
    Task<LibrarySettings> GetAsync(CancellationToken ct);
    Task SaveAsync(LibrarySettings settings, string actor, CancellationToken ct);
}

public interface ILibrarySettingsRepository
{
    Task<LibrarySettings> GetAsync(LendingOptions defaults, CancellationToken ct);
    Task SaveAsync(LibrarySettings settings, string actor, CancellationToken ct);
}
