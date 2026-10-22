using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Enums;
using LibraryManagement.Domain.Exceptions;
using LibraryManagement.Domain.Rules;
using LibraryManagement.Application.DTOs;
using LibraryManagement.Application.Interfaces;
using LibraryManagement.Application.Services;
using Moq;
using Xunit;
namespace LibraryManagement.Core.Tests;
public sealed class ModuleHierarchyTests
{
    [Fact] public void Hierarchy_orders_groups_before_children_and_builds_full_path()
    {
        ModulePath group=new(){Id=1,DisplayName="Collection",IsLeafNode=false};ModulePath leaf=new(){Id=2,DisplayName="Books",ControllerName="Books",ParentId=1};
        Assert.Equal(new[]{1,2},ModuleHierarchy.Ordered(new[]{leaf,group}).Select(m=>m.Id));Assert.Equal("Collection / Books",ModuleHierarchy.FullPath(leaf,new[]{group,leaf}));
    }
    [Fact] public void Cycles_missing_parents_and_leaf_parents_are_rejected()
    {
        ModulePath group=new(){Id=1,IsLeafNode=false,ParentId=2};ModulePath other=new(){Id=2,IsLeafNode=false,ParentId=1};
        Assert.Throws<BusinessException>(()=>ModuleHierarchy.Validate(group,new[]{group,other}));other.ParentId=null;other.IsLeafNode=true;Assert.Throws<BusinessException>(()=>ModuleHierarchy.Validate(group,new[]{group,other}));
        group.ParentId=99;Assert.Throws<BusinessException>(()=>ModuleHierarchy.Validate(group,new[]{group,other}));
    }
    [Fact] public void Disabled_parent_denies_descendant_pages_and_page_in_role_is_admin_only()
    {
        ModulePath parent=new(){Id=1,IsLeafNode=false,IsActive=false};ModulePath child=new(){Id=2,ParentId=1};Assert.False(ModuleHierarchy.Available(child,new[]{parent,child}));
        Assert.Equal(ModuleRight.None,ModuleRules.RoleLimit("PageInRole",Roles.Librarian));Assert.Equal(ModuleRight.None,ModuleRules.RoleLimit("PageInRole",Roles.Member));
    }
    [Fact] public async Task Role_defaults_apply_only_until_a_user_has_custom_rights()
    {
        var repo=new Mock<IModuleAccessRepository>();ModulePath book=new(){Id=1,ControllerName="Books",IsRead=true,IsEdit=true};repo.Setup(r=>r.ModulesAsync(default)).ReturnsAsync(new[]{book});
        repo.Setup(r=>r.UserAsync("inherited",default)).ReturnsAsync(new UserModuleAccess{IsActive=true,Role=Roles.Librarian});repo.Setup(r=>r.UserAsync("custom",default)).ReturnsAsync(new UserModuleAccess{IsActive=true,Role=Roles.Librarian,Revision=1,Grants=[new(){ModuleId=1,IsRead=true,IsEdit=true}]});
        repo.Setup(r=>r.RoleAsync(Roles.Librarian,default)).ReturnsAsync(new RoleModuleAccess{Role=Roles.Librarian,Revision=1,Grants=[new(){ModuleId=1,IsRead=true}]});var service=new ModuleAccessService(repo.Object);
        Assert.True(await service.AllowsAsync("inherited","Books",ModuleRight.Read,default));Assert.False(await service.AllowsAsync("inherited","Books",ModuleRight.Edit,default));Assert.True(await service.AllowsAsync("custom","Books",ModuleRight.Edit,default));
    }
}
