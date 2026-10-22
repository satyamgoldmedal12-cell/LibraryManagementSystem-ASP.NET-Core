namespace LibraryManagement.Web.ViewModels;
public sealed class UserModuleAccessEditViewModel
{
    public UserModuleAccess Access { get; set; } = new();
    public IReadOnlyList<ModulePath> Modules { get; set; } = [];
}
