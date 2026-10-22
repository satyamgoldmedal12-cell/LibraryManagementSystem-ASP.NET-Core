namespace LibraryManagement.Web.ViewModels;

public sealed record CirculationLedgerViewModel(CirculationFilter Filter, CirculationPage Page, string Kind, string? BookLabel, string? MemberLabel);
