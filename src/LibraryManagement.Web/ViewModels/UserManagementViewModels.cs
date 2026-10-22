namespace LibraryManagement.Web.ViewModels;
public sealed record UserListViewModel(UserFilter Filter,PagedResult<UserSummary> Users);
public sealed record UserActivityViewModel(UserActivityFilter Filter,PagedResult<UserActivity> Activity,string? UserLabel);
public sealed record UserAccessViewModel(UserSummary User,bool Enabled);
