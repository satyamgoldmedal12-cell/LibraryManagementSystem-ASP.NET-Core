namespace LibraryManagement.Application.Services;

public sealed class BookManagementService(ILibraryRepository repository, IBookCoverProcessor images) : IBookManagementService
{
    public Task<int> SaveAsync(Book book, BookCoverUpload? cover, bool removeCover, string actor, CancellationToken ct)
    {
        BookValidation.NormalizeAndValidate(book, actor);

        if (cover is not null && removeCover) throw new BusinessException("Choose a new cover or remove the current cover, not both.");

        if (cover is not null && (cover.Content.Length == 0 || cover.Content.Length > BookCoverLimits.MaxUploadBytes))
        {
            throw new BusinessException("Cover images must be between 1 byte and 2 MB.");
        }

        var normalized = cover is null ? null : images.Process(cover);
        return repository.SaveBookWithCoverAsync(book, normalized, removeCover, actor, ct);
    }

    public Task DeleteAsync(int id, int revision, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor) || id < 1 || revision < 1) throw new BusinessException("Reload the book and try again.");
        return repository.DeleteBookAsync(id, revision, actor, ct);
    }
}
