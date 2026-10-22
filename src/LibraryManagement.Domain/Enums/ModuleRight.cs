namespace LibraryManagement.Domain.Enums;

[Flags]
public enum ModuleRight { None=0, Read=1, Add=2, Edit=4, Delete=8, Export=16, Import=32, Approval1=64, Approval2=128, All=255 }
