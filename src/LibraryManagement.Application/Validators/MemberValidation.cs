using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Application.Validators;

internal static class MemberValidation
{
    public static void NormalizeAndValidate(Member member, string actor, DateTime today)
    {
        if (string.IsNullOrWhiteSpace(actor)) throw new BusinessException("Sign in before making changes.");

        member.FullName = (member.FullName ?? "").Trim();
        member.Email = (member.Email ?? "").Trim().ToLowerInvariant();
        member.MemberNumber = (member.MemberNumber ?? "").Trim().ToUpperInvariant();
        member.Phone = member.Phone?.Trim();
        member.Address = member.Address?.Trim();

        if (member.Id == 0 && member.JoinedOn is null) member.JoinedOn = today.Date;

        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(member, new ValidationContext(member), results, true))
            throw new BusinessException(results[0].ErrorMessage ?? "Invalid member details.");

        if (member.JoinedOn is DateTime joined)
        {
            if (joined.Date > today.Date) throw new BusinessException("Membership date cannot be in the future.");
            if (member.ExpiresOn.Date < joined.Date) throw new BusinessException("Membership expiry cannot be before the membership date.");
        }

        LendingRules.EnsureNewMembershipExpiry(member.Id, member.ExpiresOn, today);
    }
}
