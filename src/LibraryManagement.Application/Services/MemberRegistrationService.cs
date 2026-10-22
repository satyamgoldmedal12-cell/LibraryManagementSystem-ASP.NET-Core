using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Application.Services;

public sealed class MemberRegistrationService(IMemberAccountProvisioner accounts) : IMemberRegistrationService
{
    public Task<OperationResult> RegisterAsync(RegisterModel model)
    {
        var validation = new List<ValidationResult>();

        if (!Validator.TryValidateObject(model, new ValidationContext(model), validation, true))
        {
            return Task.FromResult(new OperationResult(
                false,
                validation
                    .Select(x => new ServiceError("InvalidRegistration", x.ErrorMessage ?? "Check your registration details."))
                    .ToArray()));
        }

        return accounts.CreateAsync(model.FullName.Trim(), model.Email.Trim().ToLowerInvariant(), model.Password);
    }
}
