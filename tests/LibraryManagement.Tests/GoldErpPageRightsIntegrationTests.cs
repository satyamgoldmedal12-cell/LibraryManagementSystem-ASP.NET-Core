using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace LibraryManagement.Tests;
[Collection("MVC host")]
public sealed class GoldErpPageRightsIntegrationTests
{
    private const string Password="Test-PageRights!8aZ29N";
    private static string Connection(){var c=Environment.GetEnvironmentVariable("LIBRARY_TEST_CONNECTION")!;if(!new SqlConnectionStringBuilder(c).InitialCatalog.EndsWith("_Test",StringComparison.Ordinal))throw new InvalidOperationException("Dedicated test database required.");return c;}
    private static WebApplicationFactory<Program> Host()=>new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:LibraryDatabase"]=Connection()})));
    private static async Task<ApplicationUser> User(WebApplicationFactory<Program> host,string role=Roles.Librarian)
    {using var scope=host.Services.CreateScope();var email=Guid.NewGuid()+"@gold-reference.example";var user=new ApplicationUser{FullName="Page rights test "+role,Email=email,UserName=email,Role=role};Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user,Password)).Succeeded);return user;}
    private static string Token(string html)=>Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    private static async Task<HttpClient> Client(WebApplicationFactory<Program> host,ApplicationUser user)
    {var c=host.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});var page=await c.GetStringAsync("/Account/Login");Assert.Equal(HttpStatusCode.Redirect,(await c.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{["Email"]=user.Email!,["Password"]=Password,["__RequestVerificationToken"]=Token(page)}))).StatusCode);return c;}
    private static List<UserModuleGrant> Grants(IReadOnlyList<ModulePath> modules,ModuleRight books)=>modules.Select(m=>{var g=new UserModuleGrant{ModuleId=m.Id,ModuleRevision=m.Revision};if(m.ControllerName=="Books")g.SetRights(books);return g;}).ToList();
    private static Dictionary<string,string> Fields(string prefix,List<UserModuleGrant> grants)
    {
        var result=new Dictionary<string,string>();for(var i=0;i<grants.Count;i++){var p=prefix+"["+i+"].";var g=grants[i];result[p+"ModuleId"]=g.ModuleId.ToString();result[p+"ModuleRevision"]=g.ModuleRevision.ToString();foreach(var field in typeof(ModulePermissions).GetProperties().Where(p=>p.PropertyType==typeof(bool)))result[p+field.Name]=((bool)field.GetValue(g)!).ToString();}return result;
    }
    private static async Task RestoreRole(WebApplicationFactory<Program> host,string role,RoleModuleAccess? original,string actor)
    {
        if(original is null){await using var sql=new SqlConnection(Connection());await sql.ExecuteAsync("DELETE library.RoleModuleAccess WHERE Role=@role;DELETE library.RoleAccessProfiles WHERE Role=@role;",new{role});}
        else{using var scope=host.Services.CreateScope();var service=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();original.Revision=(await service.RoleAsync(role,default))!.Revision;var modules=await service.ModulesAsync(default);foreach(var grant in original.Grants)grant.ModuleRevision=modules.Single(m=>m.Id==grant.ModuleId).Revision;await service.SaveRoleAsync(original,actor,default);}
    }
    [SqlFact] public async Task Page_in_role_and_module_path_alias_render_for_admin_and_staff_are_denied()
    {
        await using var host=Host();var admin=await User(host,Roles.Admin);var staff=await User(host);using var client=await Client(host,admin);
        foreach(var route in new[]{"/ModulePath","/ModulePaths/Edit","/PageInRole","/PageInRole/Edit","/PageInRole/Edit?kind=role&target=Librarian","/PageInRole/Edit?kind=user&target="+staff.Id})Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(route)).StatusCode);
        var module=await client.GetStringAsync("/ModulePath");Assert.Contains("Full",(await client.GetStringAsync("/ModulePaths/Edit")));Assert.Contains("Collection",module);Assert.Contains("Library operations",module);Assert.Contains("Administration",module);
        var tree=await client.GetStringAsync("/PageInRole/Edit?kind=user&target="+staff.Id);Assert.Contains("data-tree-toggle",tree);Assert.Contains("Approval 1",tree);Assert.Contains("Approval 2",tree);
        using var restricted=await Client(host,staff);var denied=await restricted.GetAsync("/PageInRole");Assert.Equal(HttpStatusCode.Redirect,denied.StatusCode);Assert.Contains("AccessDenied",denied.Headers.Location!.OriginalString);
    }
    [SqlFact] public async Task Role_wise_form_changes_inherited_access_without_overwriting_custom_users_and_rejects_stale_posts()
    {
        await using var host=Host();var admin=await User(host,Roles.Admin);var inherited=await User(host);var custom=await User(host);RoleModuleAccess? original;List<UserModuleGrant> rows;
        using(var scope=host.Services.CreateScope())
        {
            var s=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();original=await s.RoleAsync(Roles.Librarian,default);rows=Grants(await s.ModulesAsync(default),ModuleRight.Read);
            var u=(await s.UserAsync(custom.Id,default))!;u.Grants=Grants(await s.ModulesAsync(default),ModuleRight.Read|ModuleRight.Edit);await s.SaveUserAsync(u,admin.Id,default);
        }
        using var client=await Client(host,admin);var url="/PageInRole/SaveRole?target=Librarian";var form=await client.GetStringAsync("/PageInRole/Edit?kind=role&target=Librarian");var data=Fields("RoleAccess.Grants",rows);data["RoleAccess.Role"]=Roles.Librarian;data["RoleAccess.Revision"]=(original?.Revision??0).ToString();
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync(url,new FormUrlEncodedContent(data))).StatusCode);data["__RequestVerificationToken"]=Token(form);
        try
        {
            Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync(url,new FormUrlEncodedContent(data))).StatusCode);
            using(var scope=host.Services.CreateScope()){var s=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();Assert.True(await s.AllowsAsync(inherited.Id,"Books",ModuleRight.Read,default));Assert.False(await s.AllowsAsync(inherited.Id,"Books",ModuleRight.Edit,default));Assert.True(await s.AllowsAsync(custom.Id,"Books",ModuleRight.Edit,default));}
            await client.PostAsync(url,new FormUrlEncodedContent(data));Assert.Contains("Role rights changed",await client.GetStringAsync("/PageInRole/Edit?kind=role&target=Librarian"));
        }
        finally{await RestoreRole(host,Roles.Librarian,original,admin.Id);}
    }
    [SqlFact] public async Task User_wise_form_saves_all_flags_and_target_mismatch_cannot_change_another_user()
    {
        await using var host=Host();var admin=await User(host,Roles.Admin);var staff=await User(host);using var client=await Client(host,admin);UserModuleAccess model;
        using(var scope=host.Services.CreateScope()){var s=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();model=(await s.UserAsync(staff.Id,default))!;model.Grants=Grants(await s.ModulesAsync(default),ModuleRight.All);}
        var html=await client.GetStringAsync("/PageInRole/Edit?kind=user&target="+staff.Id);var data=Fields("Access.Grants",model.Grants);data["Access.UserId"]=staff.Id;data["Access.UserRevision"]=model.UserRevision.ToString();data["Access.Revision"]=model.Revision.ToString();data["__RequestVerificationToken"]=Token(html);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/PageInRole/SaveUser?target="+admin.Id,new FormUrlEncodedContent(data))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/PageInRole/SaveUser?target="+staff.Id,new FormUrlEncodedContent(data))).StatusCode);
        using var verify=host.Services.CreateScope();Assert.True(await verify.ServiceProvider.GetRequiredService<IModuleAccessService>().AllowsAsync(staff.Id,"Books",ModuleRight.All,default));
    }
    [SqlFact] public async Task Hierarchy_writes_reject_cycles_leaf_parents_and_group_delete_with_children()
    {
        await using var host=Host();var admin=await User(host,Roles.Admin);using var scope=host.Services.CreateScope();var s=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();var repo=scope.ServiceProvider.GetRequiredService<IModuleAccessRepository>();
        var parent=new ModulePath{DisplayName="Test group "+Guid.NewGuid().ToString("N")[..8],IsLeafNode=false};parent.Id=await s.SaveModuleAsync(parent,admin.Id,default);
        var child=new ModulePath{DisplayName="Test nested group",ParentId=parent.Id,IsLeafNode=false};child.Id=await s.SaveModuleAsync(child,admin.Id,default);
        try
        {
            var current=(await s.ModulesAsync(default)).Single(x=>x.Id==parent.Id);current.ParentId=child.Id;
            await Assert.ThrowsAsync<BusinessException>(()=>s.SaveModuleAsync(current,admin.Id,default));await Assert.ThrowsAsync<BusinessException>(()=>repo.SaveModuleAsync(current,admin.Id,default));
            await Assert.ThrowsAsync<BusinessException>(()=>s.DeleteModuleAsync(parent.Id,current.Revision,admin.Id,default));
            var book=(await s.ModulesAsync(default)).Single(m=>m.ControllerName=="Books");var invalid=new ModulePath{DisplayName="Invalid group",IsLeafNode=false,ParentId=book.Id};await Assert.ThrowsAsync<BusinessException>(()=>s.SaveModuleAsync(invalid,admin.Id,default));
        }
        finally
        {
            var rows=await repo.ModulesAsync(default);await repo.DeleteModuleAsync(child.Id,rows.Single(m=>m.Id==child.Id).Revision,admin.Id,default);rows=await repo.ModulesAsync(default);await repo.DeleteModuleAsync(parent.Id,rows.Single(m=>m.Id==parent.Id).Revision,admin.Id,default);
        }
    }
    [SqlFact] public async Task Disabled_parent_denies_its_pages_and_menu_until_restored()
    {
        await using var host=Host();var admin=await User(host,Roles.Admin);var staff=await User(host);int groupId;int bookId;int? oldParent;
        using(var scope=host.Services.CreateScope())
        {
            var s=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();groupId=await s.SaveModuleAsync(new(){DisplayName="Inactive parent test",IsLeafNode=false},admin.Id,default);var book=(await s.ModulesAsync(default)).Single(m=>m.ControllerName=="Books");bookId=book.Id;oldParent=book.ParentId;book.ParentId=groupId;await s.SaveModuleAsync(book,admin.Id,default);var group=(await s.ModulesAsync(default)).Single(m=>m.Id==groupId);group.IsActive=false;await s.SaveModuleAsync(group,admin.Id,default);
        }
        try{using var scope=host.Services.CreateScope();var s=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();Assert.False(await s.AllowsAsync(staff.Id,"Books",ModuleRight.Read,default));Assert.DoesNotContain(await s.MenuAsync(staff.Id,default),m=>m.ControllerName=="Books");}
        finally{using var scope=host.Services.CreateScope();var s=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();var book=(await s.ModulesAsync(default)).Single(m=>m.Id==bookId);book.ParentId=oldParent;await s.SaveModuleAsync(book,admin.Id,default);var group=(await s.ModulesAsync(default)).Single(m=>m.Id==groupId);await s.DeleteModuleAsync(groupId,group.Revision,admin.Id,default);}
    }
    [SqlFact] public async Task Role_repository_enforces_admin_capabilities_and_rolls_back_failed_audit()
    {
        await using var host=Host();var admin=await User(host,Roles.Admin);var staff=await User(host);using var scope=host.Services.CreateScope();var s=scope.ServiceProvider.GetRequiredService<IModuleAccessService>();var repo=scope.ServiceProvider.GetRequiredService<IModuleAccessRepository>();var previous=await s.RoleAsync(Roles.Member,default);var model=new RoleModuleAccess{Role=Roles.Member,Revision=previous?.Revision??0,Grants=Grants(await s.ModulesAsync(default),ModuleRight.Read)};
        await Assert.ThrowsAsync<BusinessException>(()=>repo.SaveRoleAsync(model,staff.Id,default));model.Grants.Single(g=>g.IsRead).IsEdit=true;await Assert.ThrowsAsync<BusinessException>(()=>repo.SaveRoleAsync(model,admin.Id,default));model.Grants.Single(g=>g.IsRead).IsEdit=false;
        await using var sql=new SqlConnection(Connection());await sql.ExecuteAsync("CREATE OR ALTER TRIGGER library.Test_FailRoleAudit ON library.AuditEvents AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Action='Role rights assigned') THROW 50001,'Simulated role audit failure.',1;END;");
        try{await Assert.ThrowsAsync<BusinessException>(()=>repo.SaveRoleAsync(model,admin.Id,default));}
        finally{await sql.ExecuteAsync("DROP TRIGGER IF EXISTS library.Test_FailRoleAudit");}
        Assert.Equal(previous?.Revision??0,await sql.ExecuteScalarAsync<int>("SELECT COALESCE((SELECT Revision FROM library.RoleAccessProfiles WHERE Role='Member'),0)"));
    }
}
