using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Domain.Entities;

public sealed class Book : AuditedEntity
{
    public int Id { get; set; }
    [Required, RegularExpression(@"\d{13}", ErrorMessage = "Enter a 13-digit ISBN."), StringLength(13)]
    public string ISBN { get; set; } = "";
    [Required, StringLength(200)] public string Title { get; set; } = "";
    public int AuthorId { get; set; }
    public int? PublisherId { get; set; }
    [Required, StringLength(150)] public string Author { get; set; } = "";
    [StringLength(150)] public string? Publisher { get; set; }
    [Range(1, int.MaxValue)] public int CategoryId { get; set; }
    [Range(1, 10000)] public int TotalCopies { get; set; } = 1;
    public bool HasCover { get; set; }
    public string Status => IsArchived ? "Archived" : AvailableCopies > 0 ? "Available" : "All copies issued";
    public bool IsArchived { get; set; }
    public int Revision { get; set; }
    public string? CategoryName { get; set; }
    public int AvailableCopies { get; set; }
}
