using LibraryManagement.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagement.Web.Controllers;

[Authorize(Roles = Roles.Admin)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class UsersController(IUserAdministrationService accounts, ILogger<UsersController> logger, ILibraryQueries queries) : LibraryController
{
    public async Task<IActionResult> Index([FromQuery] UserFilter filter,CancellationToken ct)
    {
        PagedResult<UserSummary> users=new([],0,Math.Clamp(filter.Page,1,100000),20);
        if(ModelState.IsValid){try{users=await accounts.PageAsync(filter,ct);}catch(BusinessException ex){ModelState.AddModelError("",ex.Message);}}
        return View(new UserListViewModel(filter,users));
    }
    [HttpGet]
    public async Task<IActionResult> Edit(string id,CancellationToken ct)
    {
        var user=await accounts.SummaryAsync(id,ct);if(user is null)return NotFound();
        await MemberLabel(user.MemberId,ct);
        return View(new EditUserModel{Id=user.Id,FullName=user.FullName,Email=user.Email??"",Role=user.Role,MemberId=user.MemberId,Revision=user.Revision});
    }
    [HttpPost]
    public async Task<IActionResult> Edit(string id,EditUserModel model,CancellationToken ct)
    {
        if(id!=model.Id)return BadRequest();
        if(ModelState.IsValid)
        {
            var result=await accounts.EditAsync(model,Actor,ct);if(result.IsNotFound)return NotFound();
            if(result.Succeeded){Success("Account updated. Existing sessions have been signed out.");return RedirectToAction(nameof(Index));}
            foreach(var error in result.Errors)ModelState.AddModelError("",error.Description);
        }
        await MemberLabel(model.MemberId,ct);return View(model);
    }
    [HttpGet]
    public async Task<IActionResult> Access(string id,bool enabled,CancellationToken ct)
    {
        var user=await accounts.SummaryAsync(id,ct);return user is null?NotFound():View(new UserAccessViewModel(user,enabled));
    }
    [HttpGet]
    public async Task<IActionResult> Activity([FromQuery] UserActivityFilter filter,CancellationToken ct)
    {
        PagedResult<UserActivity> rows=new([],0,Math.Clamp(filter.Page,1,100000),20);
        if(ModelState.IsValid){try{rows=await accounts.ActivityAsync(filter,ct);}catch(BusinessException ex){ModelState.AddModelError("",ex.Message);}}
        var user=filter.UserId is null?null:await accounts.SummaryAsync(filter.UserId,ct);
        return View(new UserActivityViewModel(filter,rows,user is null?null:user.FullName+" — "+user.Email));
    }
    [HttpGet]
    public async Task<IActionResult> Lookup(string? search,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(search)||search.Trim().Length<2)return Json(Array.Empty<object>());
        var page=await accounts.PageAsync(new(){Search=SearchTerm(search)},ct);return Json(page.Items.Select(u=>new{u.Id,text=u.FullName+" — "+u.Email+" ("+u.Role+")"}));
    }
    private async Task MemberLabel(int? id,CancellationToken ct)
    {
        var member=id.HasValue?await queries.MemberAsync(id.Value,ct):null;ViewBag.MemberLabel=member is null?null:member.MemberNumber+" — "+member.FullName;
    }

    public IActionResult Create() => View(new CreateUserModel());

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) { await MemberLabel(model.MemberId,ct); return View(model); }

        var result = await accounts.CreateAsync(model, Actor, ct);

        if (result.Succeeded)
        {
            logger.LogInformation("Administrator {ActorId} created account with role {Role}", Actor, model.Role);
            Success("Account created.");
            return RedirectToAction(nameof(Index));
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(error.Code == "MemberId" ? nameof(model.MemberId) : "", error.Description);
        }

        await MemberLabel(model.MemberId,ct);return View(model);
    }

    public async Task<IActionResult> ResetPassword(string id,CancellationToken ct)
    {
        var user=await accounts.SummaryAsync(id,ct);if(user is null)return NotFound();ViewBag.UserLabel=user.FullName+" — "+user.Email;
        return View(new ResetPasswordModel{Id=id});
    }

    [HttpPost]
    public async Task<IActionResult> ResetPassword(ResetPasswordModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await accounts.ResetPasswordAsync(model, Actor, ct);

        if (result.IsNotFound) return NotFound();

        if (result.Succeeded)
        {
            logger.LogInformation("Administrator {ActorId} reset password for user {UserId}", Actor, model.Id);
            Success("Password reset. Existing sessions have been signed out.");
            return RedirectToAction(nameof(Index));
        }

        foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> SetAccess(string id, bool? enabled, CancellationToken ct)
    {
        if(!ModelState.IsValid || string.IsNullOrWhiteSpace(id) || !enabled.HasValue)return BadRequest();
        var result = await accounts.SetAccessAsync(id, enabled.Value, Actor, ct);

        if (result.IsNotFound) return NotFound();

        if (result.Succeeded)
        {
            logger.LogInformation("Administrator {ActorId} set access {Enabled} for user {UserId}", Actor, enabled, id);
            Success("Account access updated. Existing sessions have been signed out.");
        }
        else
        {
            TempData["Error"] = result.Errors.FirstOrDefault()?.Description ?? "Account changed. Reload and try again.";
        }

        return RedirectToAction(nameof(Index));
    }
}
