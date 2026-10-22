using LibraryManagement.Application.DTOs;

namespace LibraryManagement.Web.ViewModels;

public sealed record IssueBookViewModel(IssueLoan Input,IssueAssessment Assessment);
