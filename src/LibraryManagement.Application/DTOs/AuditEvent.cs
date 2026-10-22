using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Application.DTOs;

public sealed class AuditEvent
{
    public long Id { get; set; }
    public string ActorName { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityId { get; set; } = "";
    public DateTime OccurredAt { get; set; }
}
