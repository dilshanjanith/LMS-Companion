namespace LmsCompanion.Domain;

/// <summary>A validated UTC interval for LMS list queries.</summary>
public sealed record LmsDateRange
{
    public const int MaximumDays = 90;

    private LmsDateRange(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }

    public DateTimeOffset To { get; }

    public static LmsDateRange Create(DateTimeOffset from, DateTimeOffset to)
    {
        var utcFrom = from.ToUniversalTime();
        var utcTo = to.ToUniversalTime();

        if (utcTo <= utcFrom)
        {
            throw new ArgumentException("The end of the date range must be after its start.", nameof(to));
        }

        if (utcTo - utcFrom > TimeSpan.FromDays(MaximumDays))
        {
            throw new ArgumentOutOfRangeException(nameof(to), $"The date range cannot exceed {MaximumDays} days.");
        }

        return new LmsDateRange(utcFrom, utcTo);
    }
}
