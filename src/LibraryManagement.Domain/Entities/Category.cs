using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Domain.Entities;

public sealed class Category : AuditedEntity
{
    public int Id { get; set; }
    [Required, StringLength(80)] public string Name { get; set; } = "";
}
