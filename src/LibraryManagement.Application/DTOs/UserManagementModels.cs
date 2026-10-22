using System.ComponentModel.DataAnnotations;
namespace LibraryManagement.Application.DTOs;
public sealed class EditUserModel
{
    [Required,StringLength(450)] public string Id { get; set; } = "";
    [Required,StringLength(120)] public string FullName { get; set; } = "";
    [Required,EmailAddress,StringLength(256)] public string Email { get; set; } = "";
    [Required,RegularExpression("^(Admin|Librarian|Member)$")] public string Role { get; set; } = Roles.Librarian;
    [Range(1,int.MaxValue)] public int? MemberId { get; set; }
    [Range(1,int.MaxValue)] public int Revision { get; set; }
}
public sealed class UserFilter
{
    [StringLength(200)] public string? Search { get; set; }
    public string Role { get; set; } = "all";
    public string Status { get; set; } = "all";
    [Range(1,100000)] public int Page { get; set; } = 1;
}
public sealed class UserActivityFilter
{
    [StringLength(450)] public string? UserId { get; set; }
    [StringLength(80)] public string? Action { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    [Range(1,100000)] public int Page { get; set; } = 1;
}
public sealed class UserActivity
{
    public long Id { get; set; }
    public string ActorName { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string? SubjectName { get; set; }
    public DateTime OccurredAt { get; set; }
}
