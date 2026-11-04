using System.Globalization;
using System.Text.Json.Serialization;

namespace RunnersDiary.Domain.Insights;

public enum InsightBucket
{
    [JsonStringEnumMemberName("week")] Week,
    [JsonStringEnumMemberName("month")] Month
}
public enum UnavailableReason
{
    [JsonStringEnumMemberName("missingData")] MissingData,
    [JsonStringEnumMemberName("noLoad")] NoLoad,
    [JsonStringEnumMemberName("zeroVariance")] ZeroVariance,
    [JsonStringEnumMemberName("zeroBaseline")] ZeroBaseline,
    [JsonStringEnumMemberName("zeroChronicLoad")] ZeroChronicLoad,
    [JsonStringEnumMemberName("insufficientHistory")] InsufficientHistory,
    [JsonStringEnumMemberName("noEligibleSessions")] NoEligibleSessions,
    [JsonStringEnumMemberName("nonFinite")] NonFinite
}
public enum LoadBand
{
    [JsonStringEnumMemberName("low")] Low,
    [JsonStringEnumMemberName("optimal")] Optimal,
    [JsonStringEnumMemberName("high")] High
}

/// <summary>Scalar completed-session input. Dates are UTC; no personal free text or infrastructure dependencies.</summary>
public sealed record InsightSession(
    DateOnly Date, string TypeCode, bool IsRace, double? DistanceMeters,
    int? DurationSeconds, double? ElevationGainMeters, double? ElevationLossMeters, int? Rpe);

/// <summary>Known subtotal, not an estimate. Missing eligible values are never filled with zero.</summary>
public sealed record MetricCoverage(decimal KnownSubtotal, int EligibleCount, int ContributingCount)
{
    public int MissingCount => EligibleCount - ContributingCount;
    public bool IsComplete => MissingCount == 0;
}

public sealed record DerivedMetric(double? Value, UnavailableReason? Reason);
public sealed record InsightTotals(
    int SessionCount, int RestSessionCount, MetricCoverage RunningDistanceMeters, MetricCoverage DurationSeconds,
    MetricCoverage ElevationGainMeters, MetricCoverage ElevationLossMeters, MetricCoverage Load);
public sealed record PaceMetric(DerivedMetric SecondsPerKilometer, int EligibleCount, int ContributingCount)
{
    public int MissingCount => EligibleCount - ContributingCount;
    public bool IsComplete => MissingCount == 0;
}
public sealed record InsightPeriod(DateOnly From, DateOnly To, InsightTotals Totals, PaceMetric EasyPace);
public sealed record InsightBucketResult(
    DateOnly PeriodStart, int? IsoWeekYear, int? IsoWeekNumber, InsightPeriod Period);
public sealed record MetricComparison(MetricCoverage Current, MetricCoverage Previous, DerivedMetric PercentageChange);
public sealed record PeriodComparison(
    DateOnly CurrentFrom, DateOnly CurrentTo, DateOnly PreviousFrom, DateOnly PreviousTo,
    MetricComparison RunningDistanceMeters, MetricComparison DurationSeconds,
    MetricComparison ElevationGainMeters, MetricComparison Load);
public sealed record TypeDistribution(string TypeCode, int SessionCount, double Share);
public sealed record LoadInsights(
    MetricCoverage Acute7, MetricCoverage Chronic28, decimal ChronicWeeklyKnownSubtotal,
    DerivedMetric Acwr, LoadBand? Band, UnavailableReason? BandReason, DerivedMetric Monotony,
    DateOnly? FirstCompletedActivityDate);
/// <summary>Trailing load windows anchored to Date, including lookback before the selected range.</summary>
public sealed record LoadSeriesPoint(
    DateOnly Date, MetricCoverage Acute7, MetricCoverage Chronic28, decimal ChronicWeeklyKnownSubtotal,
    DerivedMetric Acwr, LoadBand? Band, UnavailableReason? BandReason, DerivedMetric Monotony);
public sealed record InsightsResponse(
    DateOnly From, DateOnly To, InsightBucket Bucket, InsightPeriod Selected,
    IReadOnlyList<InsightPeriod> Daily, IReadOnlyList<InsightBucketResult> Buckets,
    InsightPeriod YearToDate, PeriodComparison WeekComparison, PeriodComparison MonthComparison,
    IReadOnlyList<TypeDistribution> Distribution, LoadInsights TrainingLoad,
    IReadOnlyList<LoadSeriesPoint> LoadSeries);

/// <summary>
/// Deterministic calculations over completed scalar sessions only. Callers must supply the required
/// lookback (RequiredFrom through to) and the first completed non-rest activity date across all history.
/// Empty calendar days are zero load; sessions with missing load remain incomplete.
/// </summary>
public static class InsightsCalculator
{
    private static readonly IReadOnlyDictionary<string, RunType> Types =
        RunTypeCatalog.All.ToDictionary(type => type.Code, StringComparer.Ordinal);

    public static bool IsActivity(string code, bool isRace) =>
        code != "rest-injury" && (isRace || code == "cross-training"
            || Types.TryGetValue(code, out var type) && type.Category == "Training");

    private static bool IsRunning(InsightSession session) =>
        session.TypeCode is not ("rest-injury" or "cross-training") && (session.IsRace
            || Types.TryGetValue(session.TypeCode, out var type) && type.Category == "Training");

    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    // Domain supports year 2 onwards, leaving space for the prior period at the lower date boundary.
    public static DateOnly RequiredFrom(DateOnly from, DateOnly to)
    {
        var week = WeekStart(to).AddDays(-7);
        var month = new DateOnly(to.Year, to.Month, 1).AddMonths(-1);
        return new[] { from.AddDays(-27), new DateOnly(to.Year, 1, 1), week, month }.Min();
    }

    public static InsightsResponse Calculate(
        IReadOnlyList<InsightSession> sessions, DateOnly from, DateOnly to, InsightBucket bucket,
        DateOnly? firstCompletedActivityDate)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (from.Year < 2 || from > to || to.DayNumber - from.DayNumber > 366
            || !Enum.IsDefined(bucket))
        {
            throw new ArgumentOutOfRangeException(nameof(from));
        }

        var relevant = sessions.Where(s => s.Date <= to && IsActivity(s.TypeCode, s.IsRace)).ToArray();
        var byDay = relevant.GroupBy(s => s.Date).ToDictionary(g => g.Key, g => g.ToArray());
        var restByDay = sessions.Where(s => s.Date <= to && s.TypeCode == "rest-injury")
            .GroupBy(s => s.Date).ToDictionary(g => g.Key, g => g.Count());
        InsightPeriod Period(DateOnly start, DateOnly end)
        {
            var items = relevant.Where(s => s.Date >= start && s.Date <= end).ToArray();
            var restCount = restByDay.Where(pair => pair.Key >= start && pair.Key <= end).Sum(pair => pair.Value);
            return new(start, end, Totals(items, restCount), Pace(items));
        }

        var daily = new List<InsightPeriod>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var items = byDay.GetValueOrDefault(day) ?? [];
            daily.Add(new(day, day, Totals(items, restByDay.GetValueOrDefault(day)), Pace(items)));
            if (day == DateOnly.MaxValue) break;
        }

        var buckets = new List<InsightBucketResult>();
        for (var start = bucket == InsightBucket.Week ? WeekStart(from) : new DateOnly(from.Year, from.Month, 1);
             start <= to;)
        {
            var next = bucket == InsightBucket.Week ? start.AddDays(7) : start.AddMonths(1);
            buckets.Add(new(start,
                bucket == InsightBucket.Week ? ISOWeek.GetYear(start.ToDateTime(TimeOnly.MinValue)) : null,
                bucket == InsightBucket.Week ? ISOWeek.GetWeekOfYear(start.ToDateTime(TimeOnly.MinValue)) : null,
                Period(start < from ? from : start, next.AddDays(-1) > to ? to : next.AddDays(-1))));
            start = next;
        }

        var selected = Period(from, to);
        var distribution = relevant.Where(s => s.Date >= from).GroupBy(s => s.TypeCode)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new TypeDistribution(g.Key, g.Count(), (double)g.Count() / selected.Totals.SessionCount))
            .ToArray();

        var dailyLoads = byDay.ToDictionary(pair => pair.Key, pair => Totals(pair.Value).Load);
        LoadInsights LoadAt(DateOnly date) => CalculateLoad(date, dailyLoads, firstCompletedActivityDate);
        var loadSeries = daily.Select(day =>
        {
            var load = LoadAt(day.From);
            return new LoadSeriesPoint(day.From, load.Acute7, load.Chronic28, load.ChronicWeeklyKnownSubtotal,
                load.Acwr, load.Band, load.BandReason, load.Monotony);
        }).ToArray();

        PeriodComparison Compare(bool week)
        {
            var currentStart = week ? WeekStart(to) : new DateOnly(to.Year, to.Month, 1);
            var previousStart = week ? currentStart.AddDays(-7) : currentStart.AddMonths(-1);
            var currentEnd = week ? currentStart.AddDays(6) : currentStart.AddMonths(1).AddDays(-1);
            var previousEnd = to == currentEnd ? currentStart.AddDays(-1)
                : previousStart.AddDays(Math.Min(to.DayNumber - currentStart.DayNumber,
                    currentStart.DayNumber - previousStart.DayNumber - 1));
            var current = Period(currentStart, to).Totals;
            var previous = Period(previousStart, previousEnd).Totals;
            return new(currentStart, to, previousStart, previousEnd,
                Change(current.RunningDistanceMeters, previous.RunningDistanceMeters),
                Change(current.DurationSeconds, previous.DurationSeconds),
                Change(current.ElevationGainMeters, previous.ElevationGainMeters),
                Change(current.Load, previous.Load));
        }

        return new(from, to, bucket, selected, daily, buckets,
            Period(new DateOnly(to.Year, 1, 1), to), Compare(true), Compare(false), distribution,
            LoadAt(to), loadSeries);
    }

    private static LoadInsights CalculateLoad(
        DateOnly date, IReadOnlyDictionary<DateOnly, MetricCoverage> dailyLoads, DateOnly? firstCompletedActivityDate)
    {
        var days = Enumerable.Range(0, 28).Select(offset =>
            dailyLoads.GetValueOrDefault(date.AddDays(-offset)) ?? new MetricCoverage(0, 0, 0)).ToArray();
        static MetricCoverage Combine(IEnumerable<MetricCoverage> values)
        {
            var items = values.ToArray();
            return new(items.Sum(item => item.KnownSubtotal), items.Sum(item => item.EligibleCount),
                items.Sum(item => item.ContributingCount));
        }
        var acute = Combine(days.Take(7));
        var chronic = Combine(days);
        var acwr = !chronic.IsComplete ? Missing(UnavailableReason.MissingData)
            : chronic.KnownSubtotal == 0 ? Missing(UnavailableReason.ZeroChronicLoad)
            : Number((double)(acute.KnownSubtotal / (chronic.KnownSubtotal / 4)));
        var mature = firstCompletedActivityDate is not null
            && date.DayNumber - firstCompletedActivityDate.Value.DayNumber + 1 >= 28;
        LoadBand? band = acwr.Value is { } ratio && mature
            ? ratio < 0.8 ? LoadBand.Low : ratio <= 1.3 ? LoadBand.Optimal : LoadBand.High : null;
        var bandReason = acwr.Reason ?? (mature ? (UnavailableReason?)null : UnavailableReason.InsufficientHistory);
        var seven = days.Take(7).Select(day => day.KnownSubtotal).ToArray();
        var mean = (double)(seven.Sum() / 7);
        var variance = seven.Select(value => Math.Pow((double)value - mean, 2)).Sum() / 7;
        var monotony = !acute.IsComplete ? Missing(UnavailableReason.MissingData)
            : mean == 0 ? Missing(UnavailableReason.NoLoad)
            : variance == 0 ? Missing(UnavailableReason.ZeroVariance)
            : Number(mean / Math.Sqrt(variance));

        return new(acute, chronic, chronic.KnownSubtotal / 4, acwr, band, bandReason, monotony,
            firstCompletedActivityDate);
    }

    private static InsightTotals Totals(IReadOnlyList<InsightSession> items, int restSessionCount = 0)
    {
        var running = items.Where(IsRunning).ToArray();
        return new(items.Count, restSessionCount,
            Sum(running, s => Numeric(s.DistanceMeters)),
            Sum(items, s => s.DurationSeconds is >= 0 ? s.DurationSeconds : null),
            Sum(items, s => Numeric(s.ElevationGainMeters)),
            Sum(items, s => Numeric(s.ElevationLossMeters)),
            Sum(items, s => s.DurationSeconds is >= 0 && s.Rpe is >= 1 and <= 10
                ? (decimal)s.DurationSeconds.Value * s.Rpe.Value / 60 : null));
    }

    private static decimal? Numeric(double? value) =>
        value is >= 0 && double.IsFinite(value.Value) && value.Value <= 1e20 ? (decimal)value.Value : null;

    private static MetricCoverage Sum(IReadOnlyList<InsightSession> items, Func<InsightSession, decimal?> selector)
    {
        var values = items.Select(selector).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return new(values.Sum(), items.Count, values.Length);
    }

    private static PaceMetric Pace(IReadOnlyList<InsightSession> items)
    {
        var easy = items.Where(s => s.TypeCode == "easy-run").ToArray();
        var pairs = easy.Where(s => Numeric(s.DistanceMeters) is > 0 && s.DurationSeconds is > 0).ToArray();
        return new(pairs.Length == 0 ? Missing(UnavailableReason.NoEligibleSessions)
            : Number((double)(pairs.Sum(s => (decimal)s.DurationSeconds!.Value)
                / pairs.Sum(s => Numeric(s.DistanceMeters)!.Value) * 1000)), easy.Length, pairs.Length);
    }

    private static MetricComparison Change(MetricCoverage current, MetricCoverage previous) =>
        new(current, previous, !current.IsComplete || !previous.IsComplete ? Missing(UnavailableReason.MissingData)
            : previous.KnownSubtotal == 0 ? Missing(UnavailableReason.ZeroBaseline)
            : Number((double)((current.KnownSubtotal - previous.KnownSubtotal) / previous.KnownSubtotal * 100)));

    private static DerivedMetric Missing(UnavailableReason reason) => new(null, reason);
    private static DerivedMetric Number(double value) => double.IsFinite(value)
        ? new(value, null) : Missing(UnavailableReason.NonFinite);
}
