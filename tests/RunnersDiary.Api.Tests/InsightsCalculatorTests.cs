using RunnersDiary.Domain.Insights;

public sealed class InsightsCalculatorTests
{
    private static readonly DateOnly End = new(2026, 11, 4);
    private static InsightSession Session(DateOnly date, int? seconds = 600, int? rpe = 6,
        string type = "easy-run", double? meters = 1000, bool race = false) =>
        new(date, type, race, meters, seconds, 10, 5, rpe);

    private static InsightsResponse Calculate(InsightSession[] sessions, DateOnly? from = null,
        DateOnly? to = null, DateOnly? first = null, InsightBucket bucket = InsightBucket.Week) =>
        InsightsCalculator.Calculate(sessions, from ?? End.AddDays(-27), to ?? End, bucket, first ?? End.AddDays(-100));

    [Fact]
    public void Constant_daily_load_has_coupled_ratio_one_and_zero_variance()
    {
        var sessions = Enumerable.Range(0, 28).Select(i => Session(End.AddDays(-i))).ToArray();
        var result = Calculate(sessions);
        Assert.Equal(420, result.TrainingLoad.Acute7.KnownSubtotal);
        Assert.Equal(1680, result.TrainingLoad.Chronic28.KnownSubtotal);
        Assert.Equal(420, result.TrainingLoad.ChronicWeeklyKnownSubtotal);
        Assert.Equal(1d, result.TrainingLoad.Acwr.Value);
        Assert.Equal(LoadBand.Optimal, result.TrainingLoad.Band);
        Assert.Equal(UnavailableReason.ZeroVariance, result.TrainingLoad.Monotony.Reason);
    }

    [Fact]
    public void Every_series_point_uses_full_trailing_windows_and_matches_independent_numerical_fixture()
    {
        var start = new DateOnly(2026, 1, 2);
        // Loads 1..35, with the first 27 sessions preceding the selected range.
        var sessions = Enumerable.Range(1, 35)
            .Select(i => Session(start.AddDays(i - 28), i * 60, 1)).ToArray();
        var result = Calculate(sessions, start, start.AddDays(7), sessions[0].Date);
        Assert.True(InsightsCalculator.RequiredFrom(start, start.AddDays(7)) <= start.AddDays(-27));
        Assert.Equal(8, result.LoadSeries.Count);
        for (var index = 0; index < result.LoadSeries.Count; index++)
        {
            var point = result.LoadSeries[index];
            var lastLoad = 28 + index;
            var acute = 7 * (lastLoad - 3);
            var chronic = 14 * (2 * lastLoad - 27);
            Assert.Equal(start.AddDays(index), point.Date);
            Assert.Equal(new MetricCoverage(acute, 7, 7), point.Acute7);
            Assert.Equal(new MetricCoverage(chronic, 28, 28), point.Chronic28);
            Assert.Equal(chronic / 4m, point.ChronicWeeklyKnownSubtotal);
            Assert.Equal(acute / (chronic / 4d), point.Acwr.Value!.Value, 12);
            // Seven consecutive integers have population SD=2.
            Assert.Equal((lastLoad - 3) / 2d, point.Monotony.Value!.Value, 12);
            Assert.Equal(LoadBand.High, point.Band);
            Assert.Null(point.BandReason);
        }
        var last = result.LoadSeries[^1];
        Assert.Equal(result.TrainingLoad.Acute7, last.Acute7);
        Assert.Equal(result.TrainingLoad.Chronic28, last.Chronic28);
        Assert.Equal(result.TrainingLoad.Acwr, last.Acwr);
        Assert.Equal(result.TrainingLoad.Monotony, last.Monotony);
    }

    [Fact]
    public void Series_missing_coverage_expires_on_exact_window_boundaries()
    {
        var start = End.AddDays(-30);
        var result = Calculate([Session(start, rpe: null), Session(start.AddDays(1), 60, 1)],
            start, End, start.AddDays(-100));
        Assert.Equal(1, result.LoadSeries[0].Acute7.MissingCount);
        Assert.Equal(UnavailableReason.MissingData, result.LoadSeries[6].Monotony.Reason);
        Assert.Equal(0, result.LoadSeries[7].Acute7.MissingCount);
        Assert.NotNull(result.LoadSeries[7].Monotony.Value);
        Assert.Equal(UnavailableReason.MissingData, result.LoadSeries[27].Acwr.Reason);
        Assert.Equal(new MetricCoverage(1, 1, 1), result.LoadSeries[28].Chronic28);
        Assert.Equal(0d, result.LoadSeries[28].Acwr.Value);
        Assert.Equal(LoadBand.Low, result.LoadSeries[28].Band);
        Assert.Equal(UnavailableReason.ZeroChronicLoad, result.LoadSeries[29].Acwr.Reason);
        Assert.Equal(UnavailableReason.NoLoad, result.LoadSeries[29].Monotony.Reason);
    }

    [Fact]
    public void Series_band_maturity_is_evaluated_per_date_not_at_range_end()
    {
        var result = Calculate([Session(End.AddDays(-1)), Session(End)], End.AddDays(-1), End,
            End.AddDays(-27));
        Assert.Null(result.LoadSeries[0].Band);
        Assert.Equal(UnavailableReason.InsufficientHistory, result.LoadSeries[0].BandReason);
        Assert.Equal(LoadBand.High, result.LoadSeries[1].Band);
    }

    [Fact]
    public void Monotony_uses_seven_days_population_sd_and_load_preserves_fractional_minutes()
    {
        // Loads 10,20,...,70 => mean 40, population SD 20, monotony 2.
        var sessions = Enumerable.Range(1, 7).Select(i => Session(End.AddDays(i - 7), i * 600, 1)).ToArray();
        Assert.Equal(2d, Calculate(sessions).TrainingLoad.Monotony.Value);
        var fractional = Calculate([Session(End, 61, 3)]);
        Assert.Equal(3.05m, fractional.Selected.Totals.Load.KnownSubtotal);
    }

    [Fact]
    public void Missing_load_is_not_zero_filled_and_twenty_eight_day_missing_blocks_acwr()
    {
        var result = Calculate([Session(End), Session(End.AddDays(-15), rpe: null)]);
        Assert.Equal(60, result.TrainingLoad.Chronic28.KnownSubtotal);
        Assert.Equal(2, result.TrainingLoad.Chronic28.EligibleCount);
        Assert.Equal(1, result.TrainingLoad.Chronic28.MissingCount);
        Assert.False(result.TrainingLoad.Chronic28.IsComplete);
        Assert.Equal(UnavailableReason.MissingData, result.TrainingLoad.Acwr.Reason);
        Assert.NotNull(result.TrainingLoad.Monotony.Value);
        var recent = Calculate([Session(End, seconds: null), Session(End, rpe: null)]);
        Assert.Equal(2, recent.TrainingLoad.Acute7.MissingCount);
        Assert.Equal(UnavailableReason.MissingData, recent.TrainingLoad.Monotony.Reason);
    }

    [Fact]
    public void Acute_and_chronic_windows_are_inclusive_and_exclude_the_previous_day()
    {
        var result = Calculate([
            Session(End, 60, 1),
            Session(End.AddDays(-6), 120, 1),
            Session(End.AddDays(-7), 240, 1),
            Session(End.AddDays(-27), 480, 1),
            Session(End.AddDays(-28), rpe: null)]);
        Assert.Equal(3, result.TrainingLoad.Acute7.KnownSubtotal);
        Assert.Equal(15, result.TrainingLoad.Chronic28.KnownSubtotal);
        Assert.Equal(2, result.TrainingLoad.Acute7.EligibleCount);
        Assert.Equal(4, result.TrainingLoad.Chronic28.EligibleCount);
        Assert.True(result.TrainingLoad.Chronic28.IsComplete);
        Assert.Equal(0.8d, result.TrainingLoad.Acwr.Value);
    }

    [Fact]
    public void Empty_days_are_zero_and_zero_acute_with_positive_chronic_is_valid()
    {
        var empty = Calculate([]);
        Assert.Equal(28, empty.Daily.Count);
        Assert.All(empty.Daily, day => Assert.Equal(0, day.Totals.Load.KnownSubtotal));
        Assert.All(empty.Buckets, bucket => Assert.Equal(0, bucket.Period.Totals.SessionCount));
        Assert.Equal(UnavailableReason.NoLoad, empty.TrainingLoad.Monotony.Reason);
        Assert.Equal(UnavailableReason.ZeroChronicLoad, empty.TrainingLoad.Acwr.Reason);
        var inactive = Calculate([Session(End.AddDays(-7))]);
        Assert.Equal(0d, inactive.TrainingLoad.Acwr.Value);
        Assert.Equal(LoadBand.Low, inactive.TrainingLoad.Band);
    }

    [Theory]
    [InlineData(27, false)]
    [InlineData(28, true)]
    public void Bands_require_twenty_eight_calendar_days(int days, bool hasBand)
    {
        var result = Calculate([Session(End)], first: End.AddDays(1 - days));
        Assert.Equal(hasBand, result.TrainingLoad.Band.HasValue);
        Assert.Equal(4d, result.TrainingLoad.Acwr.Value);
        if (!hasBand) Assert.Equal(UnavailableReason.InsufficientHistory, result.TrainingLoad.BandReason);
    }

    [Theory]
    [InlineData(79, 321, LoadBand.Low)]
    [InlineData(80, 320, LoadBand.Optimal)]
    [InlineData(130, 270, LoadBand.Optimal)]
    [InlineData(140, 260, LoadBand.High)]
    public void Ratio_bands_use_exact_boundaries(int acute, int prior, LoadBand expected)
    {
        var result = Calculate([Session(End, acute * 60, 1),
            Session(End.AddDays(-8), prior * 60, 1)]);
        Assert.Equal(expected, result.TrainingLoad.Band);
    }

    [Fact]
    public void Classifications_coverage_distribution_and_weighted_pace_are_independent()
    {
        var result = Calculate([
            Session(End, 300, type: "easy-run", meters: 1000),
            Session(End, 900, type: "easy-run", meters: 3000),
            Session(End, null, type: "easy-run", meters: 1000),
            Session(End, 600, type: "cross-training", meters: 50000),
            Session(End, 999, type: "rest-injury", meters: 99999),
            Session(End, 300, type: "road-race", meters: 1000, race: true)]);
        Assert.Equal(5, result.Selected.Totals.SessionCount);
        Assert.Equal(1, result.Selected.Totals.RestSessionCount);
        Assert.Equal(6000, result.Selected.Totals.RunningDistanceMeters.KnownSubtotal);
        Assert.Equal(4, result.Selected.Totals.RunningDistanceMeters.EligibleCount);
        Assert.Equal(2100, result.Selected.Totals.DurationSeconds.KnownSubtotal);
        Assert.Equal(1, result.Selected.Totals.DurationSeconds.MissingCount);
        Assert.Equal(50, result.Selected.Totals.ElevationGainMeters.KnownSubtotal);
        Assert.Equal(300d, result.Selected.EasyPace.SecondsPerKilometer.Value);
        Assert.Equal(1, result.Selected.EasyPace.MissingCount);
        Assert.Equal(1d, result.Distribution.Sum(type => type.Share));
        Assert.Equal(0.2, result.Distribution.Single(type => type.TypeCode == "cross-training").Share);
    }

    [Fact]
    public void Rest_counts_are_separate_per_period_and_do_not_create_activity_or_missing_coverage()
    {
        var result = Calculate([
            Session(End, type: "rest-injury", seconds: null, rpe: null),
            Session(End, type: "rest-injury", seconds: 999, meters: 99999),
            Session(End.AddDays(-8), type: "rest-injury"),
            Session(End.AddDays(-40), type: "rest-injury"),
            Session(End.AddDays(1), type: "rest-injury")]);
        Assert.Equal(3, result.Selected.Totals.RestSessionCount);
        Assert.Equal(4, result.YearToDate.Totals.RestSessionCount);
        Assert.Equal(2, result.Daily[^1].Totals.RestSessionCount);
        Assert.Equal(3, result.Daily.Sum(day => day.Totals.RestSessionCount));
        Assert.Equal(3, result.Buckets.Sum(bucket => bucket.Period.Totals.RestSessionCount));
        Assert.Equal(0, result.Daily[0].Totals.RestSessionCount);
        Assert.Empty(result.Distribution);
        Assert.All(result.Daily, day =>
        {
            Assert.Equal(0, day.Totals.SessionCount);
            Assert.Equal(0, day.Totals.Load.KnownSubtotal);
            Assert.Equal(0, day.Totals.Load.EligibleCount);
            Assert.True(day.Totals.Load.IsComplete);
            Assert.Equal(0, day.Totals.DurationSeconds.EligibleCount);
            Assert.Equal(0, day.Totals.RunningDistanceMeters.EligibleCount);
            Assert.Equal(0, day.Totals.ElevationGainMeters.EligibleCount);
        });
    }

    [Fact]
    public void Pace_is_sum_duration_over_paired_distance_not_mean_of_paces()
    {
        var result = Calculate([Session(End, 300, meters: 1000), Session(End, 1800, meters: 3000),
            Session(End, 300, meters: 0), Session(End, 0), Session(End, 300, meters: null)]);
        Assert.Equal(525d, result.Selected.EasyPace.SecondsPerKilometer.Value);
        Assert.Equal(5, result.Selected.EasyPace.EligibleCount);
        Assert.Equal(2, result.Selected.EasyPace.ContributingCount);
    }

    [Fact]
    public void Nonfinite_numeric_input_is_missing_never_serialized_as_nonfinite()
    {
        var result = Calculate([Session(End, meters: double.NaN), Session(End, meters: double.PositiveInfinity)]);
        Assert.Equal(2, result.Selected.Totals.RunningDistanceMeters.MissingCount);
        Assert.Equal(0, result.Selected.Totals.RunningDistanceMeters.KnownSubtotal);
    }

    [Fact]
    public void Iso_weeks_leap_day_ytd_and_partial_buckets_keep_utc_calendar_boundaries()
    {
        var end = new DateOnly(2024, 3, 1);
        var result = Calculate([Session(new(2024, 2, 29)), Session(new(2023, 12, 31))],
            new(2024, 2, 28), end);
        Assert.Equal(3, result.Daily.Count);
        Assert.Equal(1, result.YearToDate.Totals.SessionCount);
        var iso = Calculate([], new(2021, 1, 1), new(2021, 1, 4));
        Assert.Equal(2020, iso.Buckets[0].IsoWeekYear);
        Assert.Equal(53, iso.Buckets[0].IsoWeekNumber);
        Assert.Equal(new DateOnly(2021, 1, 1), iso.Buckets[0].Period.From);
        Assert.Equal(1, iso.Buckets[1].IsoWeekNumber);
    }

    [Fact]
    public void Comparisons_anchor_to_to_and_full_month_uses_full_prior_month()
    {
        var partial = Calculate([], new(2024, 2, 1), new(2024, 3, 15), bucket: InsightBucket.Month);
        Assert.Equal(new DateOnly(2024, 2, 15), partial.MonthComparison.PreviousTo);
        Assert.Equal(UnavailableReason.ZeroBaseline, partial.MonthComparison.Load.PercentageChange.Reason);
        var full = Calculate([], new(2024, 3, 1), new(2024, 3, 31));
        Assert.Equal(new DateOnly(2024, 2, 29), full.MonthComparison.PreviousTo);
        Assert.Equal(new DateOnly(2024, 3, 24), full.WeekComparison.PreviousTo);
        var shorter = Calculate([], new(2024, 3, 1), new(2024, 3, 30));
        Assert.Equal(new DateOnly(2024, 2, 29), shorter.MonthComparison.PreviousTo);
    }

    [Fact]
    public void Comparisons_compute_percentage_only_for_complete_nonzero_baseline()
    {
        var result = Calculate([Session(End, 1200), Session(End.AddDays(-7), 600)]);
        Assert.Equal(100d, result.WeekComparison.Load.PercentageChange.Value);
        var missing = Calculate([Session(End, rpe: null), Session(End.AddDays(-7), 600)]);
        Assert.Equal(UnavailableReason.MissingData, missing.WeekComparison.Load.PercentageChange.Reason);
    }
}
