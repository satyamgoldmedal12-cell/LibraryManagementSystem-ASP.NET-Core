using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Application.Services;

public sealed class LibraryService(ILibraryRepository repository, LendingOptions policy, TimeProvider clock) : ILibraryService
{
    public static bool ValidIsbn(string isbn) => LendingRules.ValidIsbn(isbn);

    private static void Validate(object model, string actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) throw new BusinessException("Sign in before making changes.");

        var results = new List<ValidationResult>();

        if (!Validator.TryValidateObject(model, new ValidationContext(model), results, true))
            throw new BusinessException(results[0].ErrorMessage ?? "Invalid input.");
    }

    public Task<int> SaveBookAsync(Book book, string actor, CancellationToken ct)
    {
        BookValidation.NormalizeAndValidate(book, actor);
        return repository.SaveBookAsync(book, actor, ct);
    }

    public Task<int> SaveMemberAsync(Member member, string actor, CancellationToken ct)
    {
        MemberValidation.NormalizeAndValidate(member, actor, LibraryManagement.Domain.Rules.IndiaTime.FromUtc(clock.GetUtcNow()).Date);
        return repository.SaveMemberAsync(member, actor, ct);
    }

    public Task SaveCategoryAsync(Category category, string actor, CancellationToken ct)
    {
        category.Name = category.Name.Trim();
        Validate(category, actor);
        return repository.SaveCategoryAsync(category, actor, ct);
    }

    public Task<int> IssueAsync(IssueLoan loan, string actor, CancellationToken ct)
    {
        Validate(loan, actor);
        LendingRules.EnsureDueDate(loan.DueDate, LibraryManagement.Domain.Rules.IndiaTime.FromUtc(clock.GetUtcNow()).Date);
        return repository.IssueAsync(loan, policy, actor, ct);
    }

    public Task ReturnAsync(int id, string actor, CancellationToken ct) => repository.ReturnAsync(id, actor, ct);

    public Task PayFineAsync(int id, string actor, CancellationToken ct) => repository.PayFineAsync(id, actor, ct);
}
