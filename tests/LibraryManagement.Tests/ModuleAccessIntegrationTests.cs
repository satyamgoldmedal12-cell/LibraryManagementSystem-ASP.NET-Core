using System.Data;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using LibraryManagement.Web.Filters;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace LibraryManagement.Tests;

[Collection("MVC host")]
public sealed class ModuleAccessIntegrationTests
{
    private static string Connection()
    {
        var s=Environment.GetEnvironmentVariable("LIBRARY_TEST_CONNECTION")!;
        if(!new SqlConnectionStringBuilder(s).InitialCatalog.EndsWith("_Test",StringComparison.Ordinal))throw new InvalidOperationException("Dedicated test database required.");return s;
    }
    private static WebApplicationFactory<Program> Host()=>new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:LibraryDatabase"]=Connection()})));
    private const string Password="Test-Module!7sN54zPQ";
    private static async Task<ApplicationUser> User(WebApplicationFactory<Program> host,string role=Roles.Librarian)
    {
        using var scope=host.Services.CreateScope();var email=Guid.NewGuid()+"@module.example";var user=new ApplicationUser{FullName="Module test "+role,UserName=email,Email=email,Role=role};
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user,Password)).Succeeded);return user;
    }
    private static string Token(string page)=>Regex.Match(page,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    private static async Task<HttpClient> Client(WebApplicationFactory<Program> host,ApplicationUser user)
    {
        var client=host.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});var page=await client.GetStringAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{["Email"]=user.Email!,["Password"]=Password,["__RequestVerificationToken"]=Token(page)}))).StatusCode);return client;
    }
    private static async Task<UserModuleAccess> Matrix(IModuleAccessService service,string id,ModuleRight books=ModuleRight.None)
    {
        var user=(await service.UserAsync(id,default))!;user.Grants=(await service.ModulesAsync(default)).Select(m=>{var g=new UserModuleGrant{ModuleId=m.Id,ModuleRevision=m.Revision};if(m.ControllerName=="Books")g.SetRights(books);return g;}).ToList();return user;
    }
    private static async Task Denied(HttpClient client,string path)
    {var r=await client.GetAsync(path);Assert.Equal(HttpStatusCode.Redirect,r.StatusCode);Assert.Contains("/Account/AccessDenied",r.Headers.Location!.OriginalString);}

    [SqlFact] public async Task Read_only_grants_hide_actions_and_other_menus_and_enforce_direct_urls_and_revocations()
    {
        await using var host=Host();var admin=await User(host,Roles.Admin);var staff=await User(host);
        using(var scope=host.Services.CreateScope()){var s=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();await s.SaveUserAsync(await Matrix(s,staff.Id,ModuleRight.Read),admin.Id,default);}
        using var client=await Client(host,staff);var html=await client.GetStringAsync("/Books");var nav=Regex.Match(html,"<nav aria-label=\"Main navigation\">((?s:.*?))</nav>").Groups[1].Value;
        Assert.Contains("Book catalog",nav);Assert.DoesNotContain("Circulation",nav);Assert.DoesNotContain("Add book",html);Assert.DoesNotMatch("href=\"/Books/Edit[^\"]*\"",html);
        await Denied(client,"/Books/Edit");await Denied(client,"/Books/Edit/1");await Denied(client,"/Members");await Denied(client,"/Reports");await Denied(client,"/UserAccess");
        Assert.Equal("/Books",(await client.GetAsync("/")).Headers.Location!.OriginalString);
        using(var scope=host.Services.CreateScope()){var s=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();await s.SaveUserAsync(await Matrix(s,staff.Id),admin.Id,default);}
        await Denied(client,"/Books");Assert.Contains("No modules assigned",await client.GetStringAsync("/"));Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/Account/Sessions")).StatusCode);
    }
    [SqlFact] public async Task Administrator_forms_render_save_all_eight_rights_and_require_antiforgery()
    {
        await using var host=Host();var admin=await User(host,Roles.Admin);var staff=await User(host);using var client=await Client(host,admin);
        foreach(var route in new[]{"/ModulePaths","/ModulePaths/Edit","/UserAccess","/UserAccess/Edit/"+staff.Id})Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(route)).StatusCode);
        var routeEdit="/UserAccess/Edit/"+staff.Id;var page=await client.GetStringAsync(routeEdit);var data=new Dictionary<string,string>();
        using(var scope=host.Services.CreateScope())
        {
            var s=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();var matrix=await Matrix(s,staff.Id,ModuleRight.All);data["Access.UserId"]=staff.Id;data["Access.UserRevision"]=matrix.UserRevision.ToString();data["Access.Revision"]=matrix.Revision.ToString();
            for(var i=0;i<matrix.Grants.Count;i++)
            {var g=matrix.Grants[i];var prefix=$"Access.Grants[{i}].";data[prefix+"ModuleId"]=g.ModuleId.ToString();data[prefix+"ModuleRevision"]=g.ModuleRevision.ToString();foreach(var p in typeof(ModulePermissions).GetProperties().Where(p=>p.PropertyType==typeof(bool)))data[prefix+p.Name]=((bool)p.GetValue(g)!).ToString();}
        }
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync(routeEdit,new FormUrlEncodedContent(data))).StatusCode);
        data["__RequestVerificationToken"]=Token(page);Assert.NotEmpty(data["__RequestVerificationToken"]);
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync(routeEdit,new FormUrlEncodedContent(data))).StatusCode);
        using(var scope=host.Services.CreateScope()){var s=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();Assert.True(await s.AllowsAsync(staff.Id,"Books",ModuleRight.All,default));Assert.False(await s.AllowsAsync(staff.Id,"Reports",ModuleRight.Read,default));}
        var stale=await client.PostAsync(routeEdit,new FormUrlEncodedContent(data));Assert.Equal(HttpStatusCode.OK,stale.StatusCode);Assert.Contains("Permissions changed",await stale.Content.ReadAsStringAsync());
    }
    [SqlFact] public async Task Concurrent_first_saves_allow_one_winner_and_audit_is_transactional()
    {
        await using var host=Host();var admin=await User(host,Roles.Admin);var staff=await User(host);UserModuleAccess first;
        using(var scope=host.Services.CreateScope())first=await Matrix(scope.ServiceProvider.GetRequiredService<IModuleAccessService>(),staff.Id,ModuleRight.Read);
        async Task<bool> Save(){using var scope=host.Services.CreateScope();try{await scope.ServiceProvider.GetRequiredService<IModuleAccessService>().SaveUserAsync(first,admin.Id,default);return true;}catch(BusinessException){return false;}}
        var result=await Task.WhenAll(Save(),Save());Assert.Single(result,x=>x);
        await using var sql=new SqlConnection(Connection());Assert.Equal(1,await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM library.AuditEvents WHERE EntityId=@Id AND Action='Module rights assigned'",new{staff.Id}));
        var another=await User(host);await sql.ExecuteAsync("CREATE OR ALTER TRIGGER library.Test_FailModuleAudit ON library.AuditEvents AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Action='Module rights assigned') THROW 50001,'Simulated audit failure.',1;END;");
        try{using var scope=host.Services.CreateScope();var s=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();var matrix=await Matrix(s,another.Id);await Assert.ThrowsAsync<BusinessException>(()=>s.SaveUserAsync(matrix,admin.Id,default));}
        finally{await sql.ExecuteAsync("DROP TRIGGER IF EXISTS library.Test_FailModuleAudit");}
        Assert.Equal(0,await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM library.UserAccessProfiles WHERE UserId=@Id",new{another.Id}));
    }
    [SqlFact] public async Task Repository_rejects_nonadmin_writes_and_module_capabilities_revoke_access()
    {
        await using var host=Host();var admin=await User(host,Roles.Admin);var staff=await User(host);ModulePath book;
        using(var scope=host.Services.CreateScope())
        {
            var s=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();var matrix=await Matrix(s,staff.Id,ModuleRight.Read|ModuleRight.Edit);await s.SaveUserAsync(matrix,admin.Id,default);book=(await s.ModulesAsync(default)).Single(m=>m.ControllerName=="Books");
            await Assert.ThrowsAsync<BusinessException>(()=>scope.ServiceProvider.GetRequiredService<IModuleAccessRepository>().SaveUserAsync(matrix,staff.Id,default));
            book.IsEdit=false;await s.SaveModuleAsync(book,admin.Id,default);
        }
        try{using var scope=host.Services.CreateScope();var s=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();Assert.True(await s.AllowsAsync(staff.Id,"Books",ModuleRight.Read,default));Assert.False(await s.AllowsAsync(staff.Id,"Books",ModuleRight.Edit,default));}
        finally{using var scope=host.Services.CreateScope();var s=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();var current=(await s.ModulesAsync(default)).Single(m=>m.Id==book.Id);current.IsEdit=true;await s.SaveModuleAsync(current,admin.Id,default);}
    }
    [SqlFact] public async Task Deleted_modules_leave_the_menu_deny_urls_and_preserve_audits_and_restore()
    {
        await using var host=Host();var admin=await User(host,Roles.Admin);var staff=await User(host);ModulePath module;
        using(var scope=host.Services.CreateScope()){var s=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();module=(await s.ModulesAsync(default)).Single(x=>x.ControllerName=="Categories");await s.DeleteModuleAsync(module.Id,module.Revision,admin.Id,default);}
        try
        {
            using var client=await Client(host,staff);await Denied(client,"/Categories");using var scope=host.Services.CreateScope();var s=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();Assert.DoesNotContain(await s.MenuAsync(staff.Id,default),x=>x.ControllerName=="Categories");
            var deleted=(await s.ModulesAsync(default)).Single(x=>x.Id==module.Id);Assert.NotNull(deleted.DeletedDate);Assert.Equal(admin.Id,deleted.DeletedBy);
            var management=(await s.ModulesAsync(default)).Single(m=>m.ControllerName=="UserAccess");await Assert.ThrowsAsync<BusinessException>(()=>s.DeleteModuleAsync(management.Id,management.Revision,admin.Id,default));
        }
        finally{using var scope=host.Services.CreateScope();var s=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();var current=(await s.ModulesAsync(default)).Single(x=>x.Id==module.Id);current.IsActive=true;await s.SaveModuleAsync(current,admin.Id,default);}
    }
    [SqlFact] public async Task Add_only_cannot_edit_by_forging_bound_record_and_reports_read_cannot_export()
    {
        await using var host=Host();var admin=await User(host,Roles.Admin);var staff=await User(host);
        using(var scope=host.Services.CreateScope())
        {
            var service=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();var matrix=await Matrix(service,staff.Id,ModuleRight.Read|ModuleRight.Add);
            var report=(await service.ModulesAsync(default)).Single(x=>x.ControllerName=="Reports");matrix.Grants.Single(x=>x.ModuleId==report.Id).IsRead=true;
            await service.SaveUserAsync(matrix,admin.Id,default);
        }
        using var client=await Client(host,staff);var add=await client.GetStringAsync("/Books/Edit");
        var forged=await client.PostAsync("/Books/Edit/0",new FormUrlEncodedContent(new Dictionary<string,string>{["book.Id"]="123",["__RequestVerificationToken"]=Token(add)}));
        Assert.Equal(HttpStatusCode.Redirect,forged.StatusCode);Assert.Contains("/Account/AccessDenied",forged.Headers.Location!.OriginalString);
        var reports=await client.GetStringAsync("/Reports");Assert.DoesNotContain("Export Excel",reports);Assert.DoesNotContain("Export PDF",reports);await Denied(client,"/Reports/Export?format=excel");
        var legacy=await client.PostAsync("/Loans/Return/123",new FormUrlEncodedContent(new Dictionary<string,string>{["__RequestVerificationToken"]=Token(add)}));
        Assert.Equal(HttpStatusCode.Redirect,legacy.StatusCode);Assert.Contains("/Account/AccessDenied",legacy.Headers.Location!.OriginalString);
    }
    [Theory][InlineData("Loans","Return","Returns",ModuleRight.Edit)][InlineData("Loans","PayFine","Fines",ModuleRight.Add)][InlineData("Reports","Export","Reports",ModuleRight.Export)]
    public void Legacy_aliases_and_exports_map_to_the_target_operation(string controller,string action,string target,ModuleRight right)
    {var actual=ModulePermissionFilter.Requirement(controller,action,new Dictionary<string,object?>());Assert.Equal((target,right),actual);}
    [Fact] public void Combined_create_edit_uses_both_route_and_bound_entity_and_unknown_actions_fail_closed()
    {
        var requirement=ModulePermissionFilter.Requirement("Books","Edit",new Dictionary<string,object?>{{"id",0},{"book",new Book{Id=20}}});Assert.Equal(("Books",ModuleRight.Edit),requirement);
        Assert.Null(ModulePermissionFilter.Requirement("Unknown","Index",new Dictionary<string,object?>()));Assert.Equal(ModuleRight.None,ModulePermissionFilter.Requirement("Books","Unexpected",new Dictionary<string,object?>())!.Value.Right);
    }
}
