using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Enums;
using LibraryManagement.Domain.Exceptions;
using LibraryManagement.Domain.Rules;
using LibraryManagement.Application.Interfaces;
using LibraryManagement.Application.DTOs;
using LibraryManagement.Application.Services;
using Moq;
using Xunit;
namespace LibraryManagement.Core.Tests;
public sealed class ModuleAccessTests
{
    private static ModulePath Book()=>new(){Id=1,ControllerName="Books",DisplayName="Books",Revision=1,IsRead=true,IsAdd=true,IsEdit=true};
    private static ModuleAccessService Service(ModulePath m,UserModuleAccess user)
    {var repo=new Mock<IModuleAccessRepository>();repo.Setup(x=>x.ModulesAsync(default)).ReturnsAsync(new[]{m});repo.Setup(x=>x.UserAsync("u",default)).ReturnsAsync(user);return new(repo.Object);}
    [Fact] public async Task Unconfigured_users_receive_role_defaults_and_members_do_not_gain_staff_writes()
    {
        var service=Service(Book(),new(){Role=Roles.Member,IsActive=true});Assert.True(await service.AllowsAsync("u","Books",ModuleRight.Read,default));Assert.False(await service.AllowsAsync("u","Books",ModuleRight.Add,default));
    }
    [Fact] public async Task An_explicit_empty_profile_revokes_defaults_and_cannot_use_unknown_rights()
    {
        var service=Service(Book(),new(){Role=Roles.Librarian,IsActive=true,Revision=1});Assert.False(await service.AllowsAsync("u","Books",ModuleRight.Read,default));Assert.Empty(await service.MenuAsync("u",default));Assert.False(await service.AllowsAsync("u","Books",ModuleRight.None,default));
    }
    [Theory][InlineData(false,false)][InlineData(true,true)]
    public async Task Inactive_or_deleted_modules_deny_even_persisted_grants(bool active,bool deleted)
    {
        var m=Book();m.IsActive=active;if(deleted)m.DeletedDate=DateTime.UtcNow;
        var s=Service(m,new(){Role=Roles.Admin,IsActive=true});Assert.False(await s.AllowsAsync("u","Books",ModuleRight.Read,default));
    }
    [Fact] public async Task Module_capabilities_and_user_grants_are_intersected()
    {
        var m=Book();m.IsEdit=false;var g=new UserModuleGrant{ModuleId=1,IsRead=true,IsEdit=true};var s=Service(m,new(){Role=Roles.Librarian,IsActive=true,Revision=1,Grants=[g]});
        Assert.True(await s.AllowsAsync("u","Books",ModuleRight.Read,default));Assert.False(await s.AllowsAsync("u","Books",ModuleRight.Edit,default));
    }
    [Fact] public async Task Deactivated_admins_do_not_receive_access_administration()
    {Assert.False(await Service(Book(),new(){Role=Roles.Admin,IsActive=false}).AllowsAsync("u","UserAccess",ModuleRight.Edit,default));}
    [Fact] public async Task Member_menus_route_to_own_profile()
    {
        var m=Book();m.ControllerName="Members";var menu=await Service(m,new(){Role=Roles.Member,IsActive=true}).MenuAsync("u",default);Assert.Equal("Profile",Assert.Single(menu).ActionName);
    }
    [Fact] public async Task Stale_catalog_and_role_escalation_are_rejected_before_repository_write()
    {
        var s=Service(Book(),new(){UserId="u",Role=Roles.Member,IsActive=true});
        await Assert.ThrowsAsync<BusinessException>(()=>s.SaveUserAsync(new(){UserId="u",Grants=[new(){ModuleId=1,ModuleRevision=1,IsRead=true,IsEdit=true}]},"admin",default));
        await Assert.ThrowsAsync<BusinessException>(()=>s.SaveUserAsync(new(){UserId="u",Grants=[new(){ModuleId=1,ModuleRevision=0,IsRead=true}]},"admin",default));
    }
}
