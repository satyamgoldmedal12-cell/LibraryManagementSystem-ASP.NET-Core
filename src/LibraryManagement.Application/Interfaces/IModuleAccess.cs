namespace LibraryManagement.Application.Interfaces;

public interface IModuleAccessRepository
{
    Task<IReadOnlyList<ModulePath>> ModulesAsync(CancellationToken ct);
    Task<int> SaveModuleAsync(ModulePath module,string actor,CancellationToken ct);
    Task DeleteModuleAsync(int id,int revision,string actor,CancellationToken ct);
    Task<UserModuleAccess?> UserAsync(string userId,CancellationToken ct);
    Task SaveUserAsync(UserModuleAccess model,string actor,CancellationToken ct);
    Task<RoleModuleAccess?> RoleAsync(string role,CancellationToken ct);
    Task SaveRoleAsync(RoleModuleAccess model,string actor,CancellationToken ct);
}
public interface IModuleAccessService
{
    Task<IReadOnlyList<ModulePath>> ModulesAsync(CancellationToken ct);
    Task<int> SaveModuleAsync(ModulePath module,string actor,CancellationToken ct);
    Task DeleteModuleAsync(int id,int revision,string actor,CancellationToken ct);
    Task<UserModuleAccess?> UserAsync(string userId,CancellationToken ct);
    Task SaveUserAsync(UserModuleAccess model,string actor,CancellationToken ct);
    Task<RoleModuleAccess?> RoleAsync(string role,CancellationToken ct);
    Task SaveRoleAsync(RoleModuleAccess model,string actor,CancellationToken ct);
    Task<bool> AllowsAsync(string userId,string controller,ModuleRight right,CancellationToken ct);
    Task<IReadOnlyList<ModuleMenuItem>> MenuAsync(string userId,CancellationToken ct);
}
