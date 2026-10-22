namespace LibraryManagement.Application.DTOs;

public sealed record ServiceError(string Code, string Description);

// Application results carry no Identity, SQL, HTTP, or MVC types.
public sealed record OperationResult(bool Succeeded, IReadOnlyList<ServiceError> Errors, bool IsNotFound = false)
{
    public static OperationResult Success { get; } = new(true, []);
    public static OperationResult Failed(string code, string description) => new(false, [new(code, description)]);
    public static OperationResult NotFound { get; } = new(false, [], true);
}

public sealed class UserSummary
{
    public string Id { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string Role { get; set; } = "";
    public int? MemberId { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public bool IsActive { get; set; } = true;
    public int Revision { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
}
