using LibraryManagement.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LibraryManagement.Web.Controllers;

[Authorize(Roles=Roles.Admin)]
public sealed class UserAccessController(IModuleAccessService permissions,IUserAdministrationService users) : LibraryController
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] UserFilter filter,CancellationToken ct)
    {
        PagedResult<UserSummary> page=new([],0,Math.Clamp(filter.Page,1,100000),20);
        if(ModelState.IsValid){try{page=await users.PageAsync(filter,ct);}catch(BusinessException ex){ModelState.AddModelError("",ex.Message);}}
        return View(new UserListViewModel(filter,page));
    }
    [HttpGet]
    public async Task<IActionResult> Edit(string id,CancellationToken ct)
    {
        var access=await permissions.UserAsync(id,ct);if(access is null)return NotFound();
        var modules=await permissions.ModulesAsync(ct);
        var role=access.Revision==0?await permissions.RoleAsync(access.Role,ct):null;
        access.Grants=ModuleHierarchy.Ordered(modules).Select(m=>
        {
            var grant=new UserModuleGrant{ModuleId=m.Id,ModuleRevision=m.Revision};
            var assigned=access.Revision==0?(role is null?ModuleRules.DefaultRights(m.ControllerName,access.Role):role.Grants.SingleOrDefault(g=>g.ModuleId==m.Id)?.Rights??ModuleRight.None):access.Grants.SingleOrDefault(g=>g.ModuleId==m.Id)?.Rights??ModuleRight.None;
            grant.SetRights(m.IsLeafNode&&ModuleHierarchy.Available(m,modules)?ModuleRules.Effective(m,assigned,access.Role):ModuleRight.None);return grant;
        }).ToList();
        return View(new UserModuleAccessEditViewModel{Access=access,Modules=modules});
    }
    [HttpPost]
    public async Task<IActionResult> Edit(string id,UserModuleAccessEditViewModel model,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(id)||id!=model.Access.UserId)return BadRequest();
        if(ModelState.IsValid)
        {
            try{await permissions.SaveUserAsync(model.Access,Actor,ct);Success("User rights saved. Menu and action access update on the user's next request.");return RedirectToAction(nameof(Edit),new{id});}
            catch(BusinessException ex){ModelState.AddModelError("",ex.Message);}
        }
        var target=await permissions.UserAsync(id,ct);if(target is null)return NotFound();
        model.Access.FullName=target.FullName;model.Access.Email=target.Email;model.Access.Role=target.Role;model.Access.IsActive=target.IsActive;
        model.Modules=await permissions.ModulesAsync(ct);
        // Re-render only known IDs. Keep submitted revisions to reject stale retries.
        model.Access.Grants=model.Modules.Select(m=>model.Access.Grants.FirstOrDefault(g=>g.ModuleId==m.Id)??new UserModuleGrant{ModuleId=m.Id,ModuleRevision=m.Revision}).ToList();
        return View(model);
    }
}
