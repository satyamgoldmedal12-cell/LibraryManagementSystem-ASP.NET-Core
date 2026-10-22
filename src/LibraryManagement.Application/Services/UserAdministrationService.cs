using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Application.Services;

public sealed class UserAdministrationService(IUserAccountIdentity accounts, ILibraryRepository repository) : IUserAdministrationService
{
    public Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken ct) => accounts.ListAsync(ct);

    private async Task<OperationResult> ValidateCreation(CreateUserModel model, CancellationToken ct)
    {
        var validation = new List<ValidationResult>();

        if (!Validator.TryValidateObject(model, new ValidationContext(model), validation, true))
        {
            return new(false, validation.Select(x => new ServiceError("InvalidAccount", x.ErrorMessage ?? "Invalid account details.")).ToArray());
        }

        if (model.Role == Roles.Member)
        {
            var member = model.MemberId.HasValue ? await repository.MemberAsync(model.MemberId.Value, ct) : null;

            if (member is null || member.IsArchived || !string.Equals(member.Email, model.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult.Failed("MemberId", "Select an existing member whose email matches this account.");
            }
        }
        else if (model.MemberId is not null)
        {
            return OperationResult.Failed("MemberId", "Only Member accounts can be linked to a membership.");
        }

        return OperationResult.Success;
    }

    public async Task<OperationResult> CreateAsync(CreateUserModel model,CancellationToken ct=default)
    {
        var valid=await ValidateCreation(model,ct);return valid.Succeeded?await accounts.CreateAsync(model):valid;
    }
    public async Task<OperationResult> CreateAsync(CreateUserModel model,string actor,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(actor))return OperationResult.Failed("Actor","Sign in as an administrator.");
        var valid=await ValidateCreation(model,ct);return valid.Succeeded?await accounts.CreateAsync(model,actor,ct):valid;
    }
    public Task<UserSummary?> SummaryAsync(string id,CancellationToken ct)=>accounts.SummaryAsync(id,ct);
    public Task<PagedResult<UserSummary>> PageAsync(UserFilter filter,CancellationToken ct)
    {
        filter.Search=string.IsNullOrWhiteSpace(filter.Search)?null:filter.Search.Trim();
        if(filter.Search?.Length>200||filter.Page is <1 or >100000||filter.Role is not ("all" or Roles.Admin or Roles.Librarian or Roles.Member)||filter.Status is not ("all" or "active" or "inactive" or "locked"))throw new BusinessException("Check the user filters.");
        return accounts.PageAsync(filter,ct);
    }
    public async Task<OperationResult> EditAsync(EditUserModel model,string actor,CancellationToken ct)
    {
        model.FullName=(model.FullName??"").Trim();model.Email=(model.Email??"").Trim();
        var validation=new List<ValidationResult>();
        if(string.IsNullOrWhiteSpace(actor)||!Validator.TryValidateObject(model,new ValidationContext(model),validation,true))return OperationResult.Failed("Validation",validation.FirstOrDefault()?.ErrorMessage??"Check the account details.");
        var current=await accounts.SummaryAsync(model.Id,ct);if(current is null)return OperationResult.NotFound;
        if(model.Id==actor&&model.Role!=current.Role)return OperationResult.Failed("OwnRole","You cannot change your own role.");
        if(model.Role==Roles.Member)
        {
            var member=model.MemberId.HasValue?await repository.MemberAsync(model.MemberId.Value,ct):null;
            if(member is null||member.IsArchived||!string.Equals(member.Email,model.Email,StringComparison.OrdinalIgnoreCase))return OperationResult.Failed("MemberId","Select a non-deleted member with the same email as this account.");
        }
        else if(model.MemberId.HasValue)return OperationResult.Failed("MemberId","Only Member accounts can be linked to a membership.");
        return await accounts.EditAsync(model,actor,ct);
    }
    public Task<PagedResult<UserActivity>> ActivityAsync(UserActivityFilter filter,CancellationToken ct)
    {
        filter.UserId=string.IsNullOrWhiteSpace(filter.UserId)?null:filter.UserId.Trim();filter.Action=string.IsNullOrWhiteSpace(filter.Action)?null:filter.Action.Trim();
        filter.FromDate=filter.FromDate?.Date;filter.ToDate=filter.ToDate?.Date;
        if(filter.UserId?.Length>450||filter.Action?.Length>80||filter.Page is <1 or >100000||filter.FromDate>filter.ToDate)throw new BusinessException("Check the user, action, dates and page filters.");
        foreach(var date in new[]{filter.FromDate,filter.ToDate})if(date.HasValue&&(date.Value.Year<1900||date.Value.Year>9998))throw new BusinessException("Choose dates between 1900 and 9998.");
        return accounts.ActivityAsync(filter,ct);
    }
    public Task<OperationResult> ResetPasswordAsync(ResetPasswordModel model,string actor,CancellationToken ct)=>accounts.ResetPasswordAsync(model.Id,model.NewPassword,actor,ct);

    public Task<OperationResult> ResetPasswordAsync(ResetPasswordModel model, CancellationToken ct) =>
        accounts.ResetPasswordAsync(model.Id, model.NewPassword, ct);

    public Task<OperationResult> SetAccessAsync(string userId, bool enabled, string actorId, CancellationToken ct) =>
        userId == actorId ? Task.FromResult(OperationResult.Failed("OwnAccess", "You cannot change your own account access."))
            : accounts.SetAccessAsync(userId, enabled, actorId, ct);
}
