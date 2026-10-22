namespace LibraryManagement.Application.DTOs;

public sealed class UserModuleGrant : ModulePermissions
{
    public int ModuleId { get; set; }
    public int ModuleRevision { get; set; }
}
public sealed class UserModuleAccess
{
    public string UserId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string Role { get; set; } = "";
    public bool IsActive { get; set; }
    public int UserRevision { get; set; }
    public int Revision { get; set; }
    public List<UserModuleGrant> Grants { get; set; } = [];
}
public sealed record ModuleMenuItem(string ControllerName,string ActionName,string DisplayName,int Id=0,int? ParentId=null);
public sealed class RoleModuleAccess
{
    public string Role { get; set; } = "";
    public int Revision { get; set; }
    public List<UserModuleGrant> Grants { get; set; } = [];
}
