namespace LibraryManagement.Domain.Rules;

public static class LendingRules
{
    public static bool ValidIsbn(string isbn) => isbn.Length == 13
        && isbn.All(c => c is >= '0' and <= '9')
        && isbn.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum() % 10 == 0;

    public static void EnsureDueDate(DateTime dueDate, DateTime today)
    {
        if (dueDate.Date <= today.Date || dueDate.Date > today.Date.AddDays(90))
            throw new BusinessException("Due date must be within the next 90 days.");
    }
    public static void EnsureNewMembershipExpiry(int id, DateTime expiresOn, DateTime today)
    {
        if (id == 0 && expiresOn.Date < today.Date)
            throw new BusinessException("New membership cannot expire in the past.");
    }
}
