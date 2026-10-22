using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Application.Validators;

internal static class BookValidation
{
    public static void NormalizeAndValidate(Book book, string actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) throw new BusinessException("Sign in before making changes.");

        book.ISBN = (book.ISBN ?? "").Replace("-", "").Replace(" ", "");
        book.Title = (book.Title ?? "").Trim();
        book.Author = (book.Author ?? "").Trim();
        book.Publisher = book.Publisher?.Trim();

        var results = new List<ValidationResult>();

        if (!Validator.TryValidateObject(book, new ValidationContext(book), results, true))
            throw new BusinessException(results[0].ErrorMessage ?? "Invalid book details.");

        if (!LendingRules.ValidIsbn(book.ISBN)) throw new BusinessException("ISBN checksum is invalid. Enter a valid ISBN-13.");
    }
}
