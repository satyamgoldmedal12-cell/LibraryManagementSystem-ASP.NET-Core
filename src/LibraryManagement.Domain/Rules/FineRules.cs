namespace LibraryManagement.Domain.Rules;

public static class FineRules
{
    public static int LateDays(DateTime dueDate, DateTime endDate) => Math.Max(0, (endDate.Date - dueDate.Date).Days);

    public static void EnsureRate(decimal dailyFine)
    {
        if (dailyFine < 0 || dailyFine > 1000 || decimal.Round(dailyFine, 2) != dailyFine)
            throw new BusinessException("Fine per day must be between ₹0 and ₹1,000 with at most two decimal places.");
    }

    public static decimal Calculate(DateTime dueDate, DateTime endDate, decimal dailyFine)
    {
        EnsureRate(dailyFine);
        return LateDays(dueDate, endDate) * dailyFine;
    }
}
