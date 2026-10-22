namespace LibraryManagement.Web.ViewModels;
public sealed class ModulePathListViewModel
{
    public string? Search { get; set; }
    public int Page { get; set; }=1;
    public int Total { get; set; }
    public int TotalPages=>Math.Max(1,(int)Math.Ceiling(Total/25d));
    public IReadOnlyList<ModulePath> Rows { get; set; }=[];
    public IReadOnlyList<ModulePath> Catalog { get; set; }=[];
}
public sealed class PageInRoleViewModel
{
    public string Kind { get; set; }="user";
    public string Category { get; set; }="all";
    public string? Target { get; set; }
    public string? Search { get; set; }
    public UserModuleAccess? Access { get; set; }
    public RoleModuleAccess? RoleAccess { get; set; }
    public IReadOnlyList<ModulePath> Modules { get; set; }=[];
}
public sealed record RightsMatrixViewModel(List<UserModuleGrant> Grants,IReadOnlyList<ModulePath> Modules,string Prefix,string Role,bool ReadOnly);
public sealed record ModuleNavigationViewModel(IReadOnlyList<ModuleMenuItem> Pages,IReadOnlyList<ModulePath> Modules,int? ParentId=null);
