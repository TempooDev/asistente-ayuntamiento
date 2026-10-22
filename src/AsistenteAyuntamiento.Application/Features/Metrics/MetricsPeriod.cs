namespace AsistenteAyuntamiento.Application.Features.Metrics;

/// <summary>A UTC date range of the metrics page: <see cref="From"/> inclusive, <see cref="To"/> exclusive.</summary>
public sealed record MetricsPeriod(DateTime From, DateTime To)
{
    public const int DefaultDays = 30;
    public const int MaxDays = 366;

    public int Days => (int)(To - From).TotalDays;

    /// <summary>The requested dates, or the last <see cref="DefaultDays"/> days (today included) when they are missing.</summary>
    /// <exception cref="ArgumentException">The start is after the end, or the range is longer than <see cref="MaxDays"/>.</exception>
    public static MetricsPeriod Resolve(DateOnly? from, DateOnly? to, DateTime utcNow)
    {
        var end = to ?? DateOnly.FromDateTime(utcNow).AddDays(1);
        var start = from ?? end.AddDays(-DefaultDays);
        if (start > end)
        {
            throw new ArgumentException("'from' must not be after 'to'.");
        }
        if (end.DayNumber - start.DayNumber > MaxDays)
        {
            throw new ArgumentException($"The period cannot be longer than {MaxDays} days.");
        }

        return new MetricsPeriod(start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    }
}

/// <summary>Percentiles with linear interpolation, the same definition as PostgreSQL's <c>percentile_cont</c>.</summary>
public static class Percentiles
{
    /// <summary>Null for an empty list; <paramref name="sorted"/> must be in ascending order.</summary>
    public static double? Of(IReadOnlyList<double> sorted, double fraction)
    {
        if (sorted.Count == 0)
        {
            return null;
        }

        var rank = fraction * (sorted.Count - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        var value = sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);
        return Math.Round(value, 2);
    }

    public static double? Of(IEnumerable<double> values, double fraction) => Of(values.Order().ToList(), fraction);
}
