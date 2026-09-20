namespace Cx.Core.Scoring;

/// <summary>Inclusive calendar date range (UTC).</summary>
public readonly record struct DateRange(DateOnly Start, DateOnly End)
{
    public const int MaxDays = 800;

    public int Days => End.DayNumber - Start.DayNumber + 1;

    /// <summary>Half-open UTC bounds [from, to) suitable for a Mongo range query.</summary>
    public (DateTime From, DateTime To) UtcBounds() =>
        (Start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), End.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    /// <summary>The immediately preceding range of the same length (used for "vs previous period").</summary>
    public DateRange Previous() => new(Start.AddDays(-Days), Start.AddDays(-1));

    public void Validate()
    {
        if (End < Start) throw new ArgumentException("The end date must not be before the start date.");
        if (Days > MaxDays) throw new ArgumentException($"The date range must not exceed {MaxDays} days.");
    }
}

public static class PeriodResolver
{
    public static readonly IReadOnlyList<string> NamedPeriods =
        ["this_month", "last_month", "this_quarter", "last_quarter", "last_30_days", "last_90_days", "last_6_months", "this_year"];

    /// <summary>Explicit dates win; otherwise a named period; otherwise the current month to date.</summary>
    public static DateRange Resolve(string? period, DateOnly? start, DateOnly? end, DateOnly today)
    {
        DateRange range;
        if (start is not null || end is not null)
        {
            var e = end ?? today;
            range = new DateRange(start ?? e.AddDays(-29), e);
        }
        else
        {
            range = Named(string.IsNullOrWhiteSpace(period) ? "this_month" : period.Trim().ToLowerInvariant(), today);
        }

        range.Validate();
        return range;
    }

    private static DateRange Named(string period, DateOnly today)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var quarterStart = new DateOnly(today.Year, (today.Month - 1) / 3 * 3 + 1, 1);
        return period switch
        {
            "this_month" => new(monthStart, today),
            "last_month" => new(monthStart.AddMonths(-1), monthStart.AddDays(-1)),
            "this_quarter" => new(quarterStart, today),
            "last_quarter" => new(quarterStart.AddMonths(-3), quarterStart.AddDays(-1)),
            "last_30_days" => new(today.AddDays(-29), today),
            "last_90_days" => new(today.AddDays(-89), today),
            "last_6_months" => new(monthStart.AddMonths(-5), today),
            "this_year" => new(new DateOnly(today.Year, 1, 1), today),
            _ => throw new ArgumentException($"Unknown period '{period}'. Use one of: {string.Join(", ", NamedPeriods)}, or explicit start/end dates."),
        };
    }
}
