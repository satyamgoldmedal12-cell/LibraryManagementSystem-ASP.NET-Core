namespace LibraryManagement.Domain.Rules;

public static class IndiaTime
{
    private static readonly TimeZoneInfo IndiaStandardTimeZone = ResolveIndiaStandardTimeZone();

    public static DateTimeOffset Now => TimeZoneInfo.ConvertTime(DateTimeOffset.Now, IndiaStandardTimeZone);

    public static DateTime Today => Now.Date;

    public static DateTimeOffset FromUtc(DateTimeOffset utcValue) => TimeZoneInfo.ConvertTime(utcValue, IndiaStandardTimeZone);

    public static DateTime FromUtc(DateTime utcValue)
    {
        var utc = utcValue.Kind == DateTimeKind.Utc
            ? utcValue
            : DateTime.SpecifyKind(utcValue, DateTimeKind.Utc);

        return TimeZoneInfo.ConvertTimeFromUtc(utc, IndiaStandardTimeZone);
    }

    private static TimeZoneInfo ResolveIndiaStandardTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        }
    }
}
