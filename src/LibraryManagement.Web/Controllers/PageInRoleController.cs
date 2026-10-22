using LibraryManagement.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LibraryManagement.Web.Controllers;

[Authorize(Roles=Roles.Admin)]
public sealed class PageInRoleController(IModuleAccessService permissions,IUserAdministrationService users) : LibraryController
{
    [HttpGet] public IActionResult Index()=>View();

    [HttpGet]
    public IActionResult GridData()
    {
        var rows = new[]
        {
            new
            {
                ApplicationName = "LibraryManagement",
                Description = "Library administration and lending",
                Mode = "User-wise rights",
                Kind = "user",
                ActionText = "Update"
            },
            new
            {
                ApplicationName = "LibraryManagement",
                Description = "Library administration and lending",
                Mode = "Role-wise rights",
                Kind = "role",
                ActionText = "Update"
            }
        };

        return Json(new { data = rows, totalCount = rows.Length });
    }

    [HttpGet]
    public async Task<IActionResult> Targets(string? search,string category="all",CancellationToken ct=default)
    {
        if(category is not ("all" or "staff" or "member"))return BadRequest();
        if(string.IsNullOrWhiteSpace(search)||search.Trim().Length<2)return Json(Array.Empty<object>());
        var filter=new UserFilter{Search=SearchTerm(search),Role=category=="member"?Roles.Member:"all"};
        IEnumerable<UserSummary> rows;
        if(category=="staff")
        {
            var pages=await Task.WhenAll(users.PageAsync(new UserFilter{Search=filter.Search,Role=Roles.Admin},ct),users.PageAsync(new UserFilter{Search=filter.Search,Role=Roles.Librarian},ct));
            rows=pages.SelectMany(p=>p.Items).OrderBy(u=>u.FullName).Take(20);
        }
        else rows=(await users.PageAsync(filter,ct)).Items;
        return Json(rows.Select(x=>new{x.Id,text=x.FullName+" — "+x.Email+" ("+x.Role+")"}));
    }
    [HttpGet]
    public async Task<IActionResult> Edit(string kind="user",string? target=null,string category="all",CancellationToken ct=default)
    {
        if(kind is not ("user" or "role")||category is not ("all" or "staff" or "member"))return BadRequest();
        var model=new PageInRoleViewModel{Kind=kind,Target=target,Category=category,Modules=await permissions.ModulesAsync(ct)};
        if(!string.IsNullOrWhiteSpace(target))
        {
            if(kind=="user")
            {
                var user=await permissions.UserAsync(target,ct);if(user is null)return NotFound();
                model.Access=user;var role=user.Revision==0?await permissions.RoleAsync(user.Role,ct):null;
                model.Access.Grants=Expand(model.Modules,user.Role,user.Revision==0?role?.Grants:user.Grants,user.Revision==0&&role is null);
            }
            else
            {
                if(target is not (Roles.Admin or Roles.Librarian or Roles.Member))return BadRequest();
                var role=await permissions.RoleAsync(target,ct);model.RoleAccess=role??new(){Role=target};model.RoleAccess.Grants=Expand(model.Modules,target,role?.Grants,role is null);
            }
        }
        return View(model);
    }
    [HttpPost]
    public async Task<IActionResult> SaveUser(string target,[Bind(Prefix="Access")] UserModuleAccess access,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(target)||target!=access.UserId)return BadRequest();
        if(!ModelState.IsValid){TempData["Error"]="Invalid permission form. Reload and try again.";return RedirectToAction(nameof(Edit),new{kind="user",target});}
        try{await permissions.SaveUserAsync(access,Actor,ct);Success("User-wise rights saved. This custom matrix overrides role defaults.");}
        catch(BusinessException ex){TempData["Error"]=ex.Message;}
        return RedirectToAction(nameof(Edit),new{kind="user",target});
    }
    [HttpPost]
    public async Task<IActionResult> SaveRole(string target,[Bind(Prefix="RoleAccess")] RoleModuleAccess access,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(target)||target!=access.Role)return BadRequest();
        if(!ModelState.IsValid){TempData["Error"]="Invalid permission form. Reload and try again.";return RedirectToAction(nameof(Edit),new{kind="role",target});}
        try{await permissions.SaveRoleAsync(access,Actor,ct);Success("Role-wise defaults saved. Users without custom rights receive them on the next request.");}
        catch(BusinessException ex){TempData["Error"]=ex.Message;}
        return RedirectToAction(nameof(Edit),new{kind="role",target});
    }
    private static List<UserModuleGrant> Expand(IReadOnlyList<ModulePath> modules,string role,List<UserModuleGrant>? assigned,bool useDefaults) => ModuleHierarchy.Ordered(modules).Select(m=>
    {
        var g=new UserModuleGrant{ModuleId=m.Id,ModuleRevision=m.Revision};
        var rights=useDefaults?ModuleRules.DefaultRights(m.ControllerName,role):assigned?.SingleOrDefault(x=>x.ModuleId==m.Id)?.Rights??ModuleRight.None;
        g.SetRights(m.IsLeafNode&&ModuleHierarchy.Available(m,modules)?ModuleRules.Effective(m,rights,role):ModuleRight.None);return g;
    }).ToList();
}
