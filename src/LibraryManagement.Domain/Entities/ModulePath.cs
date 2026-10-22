using LibraryManagement.Domain.Enums;
using LibraryManagement.Domain.Entities;
using System.ComponentModel.DataAnnotations;
namespace LibraryManagement.Domain.Entities;

public class ModulePermissions
{
    public bool IsRead { get; set; }
    public bool IsAdd { get; set; }
    public bool IsEdit { get; set; }
    public bool IsDelete { get; set; }
    public bool IsExport { get; set; }
    public bool IsImport { get; set; }
    public bool IsApproval1 { get; set; }
    public bool IsApproval2 { get; set; }
    public ModuleRight Rights => (IsRead?ModuleRight.Read:0)|(IsAdd?ModuleRight.Add:0)|(IsEdit?ModuleRight.Edit:0)|(IsDelete?ModuleRight.Delete:0)|(IsExport?ModuleRight.Export:0)|(IsImport?ModuleRight.Import:0)|(IsApproval1?ModuleRight.Approval1:0)|(IsApproval2?ModuleRight.Approval2:0);
    public void SetRights(ModuleRight rights)
    {
        IsRead=rights.HasFlag(ModuleRight.Read); IsAdd=rights.HasFlag(ModuleRight.Add); IsEdit=rights.HasFlag(ModuleRight.Edit); IsDelete=rights.HasFlag(ModuleRight.Delete);
        IsExport=rights.HasFlag(ModuleRight.Export); IsImport=rights.HasFlag(ModuleRight.Import); IsApproval1=rights.HasFlag(ModuleRight.Approval1); IsApproval2=rights.HasFlag(ModuleRight.Approval2);
    }
}
public sealed class ModulePath : ModulePermissions
{
    public int Id { get; set; }
    [Required, StringLength(80)] public string DisplayName { get; set; } = "";
    [StringLength(80), RegularExpression(@"^[A-Za-z][A-Za-z0-9]*$")] public string ControllerName { get; set; } = "";
    [Range(0,9999)] public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public int? ParentId { get; set; }
    public bool IsLeafNode { get; set; } = true;
    [Range(1,50)] public int LabelId { get; set; } = 1;
    public string PagePath => IsLeafNode ? "/"+ControllerName+"/Index" : "#";
    public int Revision { get; set; }
    public DateTime? CreatedDate { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? DeletedDate { get; set; }
    public string? DeletedBy { get; set; }
}
