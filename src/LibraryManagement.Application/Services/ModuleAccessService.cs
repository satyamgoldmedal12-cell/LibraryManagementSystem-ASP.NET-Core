using System.ComponentModel.DataAnnotations;
namespace LibraryManagement.Application.Services;

public sealed class ModuleAccessService(IModuleAccessRepository repository) : IModuleAccessService
{
    // Scoped cache only: changes/revocations are visible on the very next request.
    private Task<IReadOnlyList<ModulePath>>? modules;
    private readonly Dictionary<string,Task<UserModuleAccess?>> users = new(StringComparer.Ordinal);
    private readonly Dictionary<string,Task<RoleModuleAccess?>> roles = new(StringComparer.Ordinal);
    public Task<RoleModuleAccess?> RoleAsync(string role,CancellationToken ct)=>roles.TryGetValue(role,out var result)?result:roles[role]=repository.RoleAsync(role,ct);
    public Task<IReadOnlyList<ModulePath>> ModulesAsync(CancellationToken ct) => modules ??= repository.ModulesAsync(ct);
    public Task<UserModuleAccess?> UserAsync(string userId,CancellationToken ct) => users.TryGetValue(userId,out var result)?result:users[userId]=repository.UserAsync(userId,ct);
    public async Task<int> SaveModuleAsync(ModulePath module,string actor,CancellationToken ct)
    {
        module.DisplayName=(module.DisplayName??"").Trim();module.ControllerName=(module.ControllerName??"").Trim();
        var errors=new List<ValidationResult>();
        if(!Validator.TryValidateObject(module,new(module),errors,true)) throw new BusinessException(errors[0].ErrorMessage!);
        var catalog=await ModulesAsync(ct);ModuleHierarchy.Validate(module,catalog);
        if(module.IsLeafNode)
        {
            var canonical=ModuleRules.Controllers.SingleOrDefault(x=>x.Equals(module.ControllerName,StringComparison.OrdinalIgnoreCase));
            if(canonical is null)throw new BusinessException("Choose a supported MVC controller name, without the Controller suffix.");module.ControllerName=canonical;
            if(!module.IsRead&&module.Rights!=ModuleRight.None)throw new BusinessException("Enable Read before other permissions.");
            if(module.IsActive&&!module.IsRead)throw new BusinessException("An active page requires Read.");
        }
        else{module.ControllerName="";module.SetRights(ModuleRight.None);}
        var id=await repository.SaveModuleAsync(module,actor,ct);modules=null;return id;
    }
    public async Task DeleteModuleAsync(int id,int revision,string actor,CancellationToken ct)
    { await repository.DeleteModuleAsync(id,revision,actor,ct);modules=null; }
    public async Task SaveUserAsync(UserModuleAccess model,string actor,CancellationToken ct)
    {
        var target=await UserAsync(model.UserId,ct) ?? throw new BusinessException("User not found.");
        if(target.Role==Roles.Admin) throw new BusinessException("Administrators retain full access to active modules.");
        var catalog=await ModulesAsync(ct);
        if(model.Grants.Count!=catalog.Count || model.Grants.Select(x=>x.ModuleId).Distinct().Count()!=catalog.Count)
            throw new BusinessException("Module list changed. Reload this page and try again.");
        foreach(var grant in model.Grants)
        {
            var module=catalog.SingleOrDefault(x=>x.Id==grant.ModuleId);
            if(module is null || grant.ModuleRevision!=module.Revision) throw new BusinessException("A module changed. Reload this page and try again.");
            var limit=module.IsLeafNode&&ModuleHierarchy.Available(module,catalog)?module.Rights & ModuleRules.RoleLimit(module.ControllerName,target.Role):ModuleRight.None;
            if((grant.Rights & ~limit)!=0) throw new BusinessException("Selected rights exceed the module capabilities or user's role.");
            if(!grant.IsRead && grant.Rights!=ModuleRight.None) throw new BusinessException("Enable Read before other permissions.");
        }
        await repository.SaveUserAsync(model,actor,ct);users.Remove(model.UserId);
    }
    public async Task SaveRoleAsync(RoleModuleAccess model,string actor,CancellationToken ct)
    {
        if(model.Role is not (Roles.Librarian or Roles.Member))throw new BusinessException("Choose Librarian or Member. Administrator rights cannot be customized.");
        var catalog=await ModulesAsync(ct);
        if(model.Grants.Count!=catalog.Count||model.Grants.Select(g=>g.ModuleId).Distinct().Count()!=catalog.Count)throw new BusinessException("Module list changed. Reload and try again.");
        foreach(var grant in model.Grants)
        {
            var module=catalog.SingleOrDefault(m=>m.Id==grant.ModuleId);
            if(module is null||module.Revision!=grant.ModuleRevision)throw new BusinessException("A module changed. Reload and try again.");
            var limit=module.IsLeafNode&&ModuleHierarchy.Available(module,catalog)?module.Rights&ModuleRules.RoleLimit(module.ControllerName,model.Role):ModuleRight.None;
            if((grant.Rights&~limit)!=0||(!grant.IsRead&&grant.Rights!=ModuleRight.None))throw new BusinessException("Rights exceed the module capabilities or role. Enable Read before other rights.");
        }
        await repository.SaveRoleAsync(model,actor,ct);roles.Remove(model.Role);
    }
    public async Task<bool> AllowsAsync(string userId,string controller,ModuleRight right,CancellationToken ct)
    {
        if(right==ModuleRight.None || (right & ~ModuleRight.All)!=0) return false;
        var user=await UserAsync(userId,ct);
        if(user is null || !user.IsActive) return false;
        if(user.Role==Roles.Admin && ModuleRules.IsManagement(controller)) return true;
        var catalog=await ModulesAsync(ct);
        var module=catalog.SingleOrDefault(x=>x.IsLeafNode&&x.ControllerName.Equals(controller,StringComparison.OrdinalIgnoreCase));
        if(module is null||!ModuleHierarchy.Available(module,catalog)) return false;
        var role=user.Revision==0&&user.Role!=Roles.Admin?await RoleAsync(user.Role,ct):null;
        var assigned=user.Role==Roles.Admin?ModuleRight.All:user.Revision==0?(role is null?ModuleRules.DefaultRights(module.ControllerName,user.Role):role.Grants.SingleOrDefault(x=>x.ModuleId==module.Id)?.Rights??ModuleRight.None):user.Grants.SingleOrDefault(x=>x.ModuleId==module.Id)?.Rights??ModuleRight.None;
        var effective=ModuleRules.Effective(module,assigned,user.Role);
        return effective.HasFlag(ModuleRight.Read | right);
    }
    public async Task<IReadOnlyList<ModuleMenuItem>> MenuAsync(string userId,CancellationToken ct)
    {
        var user=await UserAsync(userId,ct);if(user is null)return [];
        var menu=new List<ModuleMenuItem>();
        var catalog=await ModulesAsync(ct);
        foreach(var module in ModuleHierarchy.Ordered(catalog).Where(m=>m.IsLeafNode && (m.ControllerName!="UserAccess" || !catalog.Any(x=>x.ControllerName=="PageInRole"&&x.IsActive))))
            if(await AllowsAsync(userId,module.ControllerName,ModuleRight.Read,ct))
                menu.Add(new(module.ControllerName,module.ControllerName=="Members"&&user.Role==Roles.Member?"Profile":"Index",module.DisplayName,module.Id,module.ParentId));
        return menu;
    }
}
