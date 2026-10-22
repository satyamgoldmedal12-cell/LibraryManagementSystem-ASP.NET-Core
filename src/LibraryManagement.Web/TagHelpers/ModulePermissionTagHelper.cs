using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
namespace LibraryManagement.Web.TagHelpers;

[HtmlTargetElement("a",Attributes="asp-action")]
[HtmlTargetElement("form",Attributes="asp-action")]
[HtmlTargetElement(Attributes="permission-right")]
public sealed class ModulePermissionTagHelper(IModuleAccessService permissions) : TagHelper
{
    [ViewContext,HtmlAttributeNotBound] public ViewContext ViewContext { get; set; } = null!;
    [HtmlAttributeName("permission-right")] public ModuleRight? Right { get; set; }
    [HtmlAttributeName("permission-controller")] public string? PermissionController { get; set; }
    public override int Order => -2000;
    public override async Task ProcessAsync(TagHelperContext context,TagHelperOutput output)
    {
        if(ViewContext.HttpContext.User.Identity?.IsAuthenticated!=true)return;
        string? Attribute(string name)=>context.AllAttributes.TryGetAttribute(name,out var a)?a.Value?.ToString():null;
        var controller=PermissionController??Attribute("asp-controller")??ViewContext.RouteData.Values["controller"]?.ToString()??"";
        var action=Attribute("asp-action")??"";
        if(controller=="Account" || controller=="Home"&&action=="Index")return;
        var arguments=new Dictionary<string,object?>();
        if(int.TryParse(Attribute("asp-route-id"),out var id))arguments["id"]=id;
        var required=Right.HasValue?(controller,Right.Value):Filters.ModulePermissionFilter.Requirement(controller,action,arguments);
        var userId=ViewContext.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if(required is null||userId is null||!await permissions.AllowsAsync(userId,required.Value.Item1,required.Value.Item2,ViewContext.HttpContext.RequestAborted))output.SuppressOutput();
    }
}
