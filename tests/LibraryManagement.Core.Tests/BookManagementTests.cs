using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Enums;
using LibraryManagement.Domain.Exceptions;
using LibraryManagement.Domain.Rules;
using LibraryManagement.Application.Interfaces;
using LibraryManagement.Application.DTOs;
using LibraryManagement.Application.Services;
using Moq;
using Xunit;

namespace LibraryManagement.Core.Tests;

public sealed class BookManagementTests
{
    private static Book Book() => new() { ISBN = "9780132350884", Title = " Book ", Author = " Author ", CategoryId = 1, TotalCopies = 2 };
    [Fact]
    public async Task Invalid_book_never_reaches_image_processing_or_persistence()
    {
        var repo = new Mock<ILibraryRepository>(MockBehavior.Strict); var images = new Mock<IBookCoverProcessor>(MockBehavior.Strict);
        var book = Book(); book.ISBN = "invalid";
        await Assert.ThrowsAsync<BusinessException>(() => new BookManagementService(repo.Object, images.Object).SaveAsync(book, new([1], "cover.png"), false, "actor", default));
        repo.VerifyNoOtherCalls(); images.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Cover_and_book_are_sent_in_one_repository_operation()
    {
        var repo = new Mock<ILibraryRepository>(MockBehavior.Strict); var images = new Mock<IBookCoverProcessor>(MockBehavior.Strict);
        var upload = new BookCoverUpload([1], "cover.png"); var normalized = new BookCover { Content = [2] }; var book = Book();
        images.Setup(x => x.Process(upload)).Returns(normalized);
        repo.Setup(x => x.SaveBookWithCoverAsync(book, normalized, false, "actor", default)).ReturnsAsync(42);
        Assert.Equal(42, await new BookManagementService(repo.Object, images.Object).SaveAsync(book, upload, false, "actor", default));
        Assert.Equal("Book", book.Title); Assert.Equal("Author", book.Author); repo.VerifyAll(); images.VerifyAll();
    }
    [Fact]
    public async Task New_cover_and_removal_cannot_be_requested_together()
    {
        var repo = new Mock<ILibraryRepository>(MockBehavior.Strict); var images = new Mock<IBookCoverProcessor>(MockBehavior.Strict);
        await Assert.ThrowsAsync<BusinessException>(() => new BookManagementService(repo.Object, images.Object).SaveAsync(Book(), new([1], "cover.png"), true, "actor", default));
        repo.VerifyNoOtherCalls(); images.VerifyNoOtherCalls();
    }
    [Theory]
    [InlineData(0)]
    [InlineData(2097153)]
    public async Task Empty_and_oversized_covers_never_reach_the_decoder(int length)
    {
        var repo = new Mock<ILibraryRepository>(MockBehavior.Strict); var images = new Mock<IBookCoverProcessor>(MockBehavior.Strict);
        await Assert.ThrowsAsync<BusinessException>(() => new BookManagementService(repo.Object, images.Object).SaveAsync(Book(), new(new byte[length], "cover.png"), false, "actor", default));
        repo.VerifyNoOtherCalls(); images.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Deletion_passes_the_expected_revision_to_the_repository()
    {
        var repo = new Mock<ILibraryRepository>(MockBehavior.Strict); repo.Setup(x => x.DeleteBookAsync(17, 4, "actor", default)).Returns(Task.CompletedTask);
        await new BookManagementService(repo.Object, Mock.Of<IBookCoverProcessor>()).DeleteAsync(17, 4, "actor", default); repo.VerifyAll();
    }
    [Fact]
    public async Task Unauthenticated_delete_is_rejected_before_persistence()
    {
        var repo = new Mock<ILibraryRepository>(MockBehavior.Strict);
        await Assert.ThrowsAsync<BusinessException>(() => new BookManagementService(repo.Object, Mock.Of<IBookCoverProcessor>()).DeleteAsync(17, 4, "", default));
        repo.VerifyNoOtherCalls();
    }
}
