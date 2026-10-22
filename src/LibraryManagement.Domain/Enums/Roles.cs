namespace LibraryManagement.Domain.Enums;

public static class Roles
{
    public const string Admin = nameof(LibraryRole.Admin);
    public const string Librarian = nameof(LibraryRole.Librarian);
    public const string Member = nameof(LibraryRole.Member);
    public const string Staff = Admin + "," + Librarian;
}
