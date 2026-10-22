using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Application.Services;

public sealed class LibrarySettingsService(ILibrarySettingsRepository repository, LendingOptions defaults) : ILibrarySettingsService
{
    public Task<LibrarySettings> GetAsync(CancellationToken ct) => repository.GetAsync(defaults, ct);

    public Task SaveAsync(LibrarySettings settings, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor)) throw new BusinessException("Sign in before changing settings.");
        settings.LibraryName = settings.LibraryName?.Trim() ?? "";
        settings.Address = Clean(settings.Address);
        settings.Email = Clean(settings.Email);
        settings.Phone = Clean(settings.Phone);
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(settings, new ValidationContext(settings), errors, true))
            throw new BusinessException(errors[0].ErrorMessage ?? "Check the settings and try again.");
        if (decimal.Round(settings.DailyFine, 2) != settings.DailyFine)
            throw new BusinessException("Fine per day must have at most two decimal places.");
        return repository.SaveAsync(settings, actor, ct);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
