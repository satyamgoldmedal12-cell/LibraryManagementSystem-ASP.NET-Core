namespace LibraryManagement.Application.DTOs;

public sealed record BookCoverUpload(byte[] Content, string FileName);
public sealed class BookCover
{
    public byte[] Content { get; set; } = [];
    public string ContentType { get; set; } = "image/jpeg";
}
public static class BookCoverLimits
{
    public const int MaxUploadBytes = 2 * 1024 * 1024;
    public const int MaxDimension = 4096;
    public const int MaxPixels = 12_000_000;

}
