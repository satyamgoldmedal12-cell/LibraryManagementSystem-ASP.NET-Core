using LibraryManagement.Domain.Exceptions;
using LibraryManagement.Domain.Entities;
namespace LibraryManagement.Domain.Rules;
public static class ModuleHierarchy
{
    public static IReadOnlyList<ModulePath> Ordered(IReadOnlyList<ModulePath> modules)
    {
        var result=new List<ModulePath>();var seen=new HashSet<int>();
        void Visit(int? parent){foreach(var m in modules.Where(m=>m.ParentId==parent).OrderBy(m=>m.SortOrder).ThenBy(m=>m.LabelId).ThenBy(m=>m.DisplayName)){if(!seen.Add(m.Id))continue;result.Add(m);if(seen.Count<=modules.Count)Visit(m.Id);}}
        Visit(null);foreach(var m in modules.Where(m=>!seen.Contains(m.Id)))result.Add(m);return result;
    }
    public static IReadOnlyList<ModulePath> Ancestors(ModulePath module,IReadOnlyList<ModulePath> modules)
    {
        var result=new List<ModulePath>();var seen=new HashSet<int>{module.Id};var id=module.ParentId;
        while(id.HasValue)
        {var parent=modules.SingleOrDefault(x=>x.Id==id.Value);if(parent is null||!seen.Add(parent.Id)||result.Count>=64)throw new BusinessException("Invalid module hierarchy.");result.Add(parent);id=parent.ParentId;}
        result.Reverse();return result;
    }
    public static string FullPath(ModulePath module,IReadOnlyList<ModulePath> modules)=>string.Join(" / ",Ancestors(module,modules).Select(m=>m.DisplayName).Append(module.DisplayName));
    public static bool Available(ModulePath module,IReadOnlyList<ModulePath> modules)
    {
        try{return module.IsActive&&!module.DeletedDate.HasValue&&Ancestors(module,modules).All(p=>!p.IsLeafNode&&p.IsActive&&!p.DeletedDate.HasValue);}
        catch(BusinessException){return false;}
    }
    public static void Validate(ModulePath module,IReadOnlyList<ModulePath> modules)
    {
        var parents=Ancestors(module,modules);
        if(parents.Any(p=>p.IsLeafNode||p.DeletedDate.HasValue))throw new BusinessException("Choose an existing parent group.");
        if(module.Id!=0&&modules.SingleOrDefault(m=>m.Id==module.Id) is { } current && current.IsLeafNode!=module.IsLeafNode)
            throw new BusinessException("An existing page/group type cannot be changed. Create a separate entry.");
    }
}
