namespace LibraryManagement.Application.Interfaces;

public interface IBookManagementService
{
    Task<int> SaveAsync(Book book, BookCoverUpload? cover, bool removeCover, string actor, CancellationToken ct);
    Task DeleteAsync(int id, int revision, string actor, CancellationToken ct);
}
public interface IBookCoverProcessor
{
    BookCover Process(BookCoverUpload upload);
}
