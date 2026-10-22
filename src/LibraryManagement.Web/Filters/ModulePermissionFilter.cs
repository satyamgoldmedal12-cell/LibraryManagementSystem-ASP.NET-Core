using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
namespace LibraryManagement.Web.Filters;

// Executes after model binding, allowing combined Add/Edit handlers to use the bound record ID.
// Unknown actions fail closed. Account self-service and the landing page remain available.
public sealed class ModulePermissionFilter(IModuleAccessService permissions) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context,ActionExecutionDelegate next)
    {
        if(context.HttpContext.User.Identity?.IsAuthenticated!=true){await next();return;}
        if(context.ActionDescriptor is not ControllerActionDescriptor descriptor){context.Result=new ForbidResult();return;}
        var controller=descriptor.ControllerName;var action=descriptor.ActionName;
        if(controller=="Account" || controller=="Home" && action is "Error" or "Index"){await next();return;}
        var requirement=Requirement(controller,action,context.ActionArguments);
        if(controller=="Settings"&&action=="Index"&&HttpMethods.IsPost(context.HttpContext.Request.Method)) requirement=("Settings",ModuleRight.Edit);
        var userId=context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if(requirement is null || userId is null || !await permissions.AllowsAsync(userId,requirement.Value.Controller,requirement.Value.Right,context.HttpContext.RequestAborted))
        {context.Result=new ForbidResult();return;}
        await next();
    }
    public static (string Controller,ModuleRight Right)? Requirement(string controller,string action,IDictionary<string,object?> arguments)
    {
        if(controller=="PageInRole" && action is "SaveUser" or "SaveRole")return (controller,ModuleRight.Edit);
        if(controller=="PageInRole" && action=="Targets")return (controller,ModuleRight.Read);
        if(controller=="Loans"&&action=="Return")return ("Returns",ModuleRight.Edit);
        if(controller=="Loans"&&action=="PayFine")return ("Fines",ModuleRight.Add);
        if(controller=="Fines"&&action=="Settings")return ("Settings",ModuleRight.Edit);
        if(!ModuleRules.Controllers.Contains(controller,StringComparer.Ordinal))return null;
        if(action=="Edit" && controller is "Books" or "Members" or "ModulePaths")
        {
            // Require Edit if either route or posted record identifies an existing entity.
            var existing=arguments.TryGetValue("id",out var id)&&id is int value&&value!=0;
            foreach(var arg in arguments.Values)
                existing|=arg is Book b&&b.Id!=0 || arg is Member m&&m.Id!=0 || arg is ModulePath p&&p.Id!=0;
            return (controller,existing?ModuleRight.Edit:ModuleRight.Add);
        }
        return (controller,action switch
        {
            "Index" or "Details" or "Profile" or "Cover" or "Lookup" or "Receipt" or "History" or "Collection" or "Activity" or "Audit" => ModuleRight.Read,
            "Create" or "Issue" or "Availability" or "Pay" => ModuleRight.Add,
            "Edit" or "Confirm" or "Restore" or "Access" or "SetAccess" or "ResetPassword" => ModuleRight.Edit,
            "Delete" => ModuleRight.Delete,
            "Export" => ModuleRight.Export,
            "Import" => ModuleRight.Import,
            "Approval1" => ModuleRight.Approval1,
            "Approval2" => ModuleRight.Approval2,
            _ => ModuleRight.None
        });
    }
}
