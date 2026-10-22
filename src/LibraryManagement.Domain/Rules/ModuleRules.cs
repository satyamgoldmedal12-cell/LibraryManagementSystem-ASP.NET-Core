using LibraryManagement.Domain.Enums;
using LibraryManagement.Domain.Entities;
namespace LibraryManagement.Domain.Rules;

// Roles are an upper bound. Per-user grants never elevate a member to a staff/admin operation.
public static class ModuleRules
{
    public static readonly string[] Controllers = ["Home","Books","Members","Loans","Returns","Fines","Reports","Categories","Users","Settings","ModulePaths","UserAccess","PageInRole"];
    public static bool IsManagement(string controller) => controller is "ModulePaths" or "UserAccess" or "PageInRole";
    public static ModuleRight RoleLimit(string controller,string role)
    {
        if (!Controllers.Contains(controller,StringComparer.OrdinalIgnoreCase)) return ModuleRight.None;
        if(role==Roles.Admin) return ModuleRight.All;
        if(controller is "Users" or "Settings" or "ModulePaths" or "UserAccess" or "PageInRole") return ModuleRight.None;
        if(role==Roles.Librarian) return ModuleRight.All;
        return role==Roles.Member && controller is "Books" or "Members" or "Loans" ? ModuleRight.Read : ModuleRight.None;
    }
    public static ModuleRight DefaultRights(string controller,string role) => RoleLimit(controller,role);
    public static ModuleRight Effective(ModulePath module,ModuleRight assigned,string role) =>
        !module.IsActive || module.DeletedDate.HasValue ? ModuleRight.None : module.Rights & assigned & RoleLimit(module.ControllerName,role);
}
