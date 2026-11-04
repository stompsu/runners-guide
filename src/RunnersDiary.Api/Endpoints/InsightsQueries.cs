using Microsoft.EntityFrameworkCore;
using RunnersDiary.Domain;
using RunnersDiary.Infrastructure;

namespace RunnersDiary.Api.Endpoints;

// SQL-translatable, owner-filtered scalar projections; intentionally separate from pure calculations.
internal static class InsightsQueries
{
    private static readonly string[] ActivityCodes = RunTypeCatalog.All
        .Where(type => type.Category == "Training" || type.Code == "cross-training")
        .Select(type => type.Code).ToArray();

    private static IQueryable<Run> OwnedCompleted(DiaryDbContext db, string owner) =>
        db.Runs.AsNoTracking().Where(run => run.UserId == owner && run.Status == RunStatus.Completed
            && !db.GpxTracks.Any(track => track.RunId == run.Id && track.DeleteParentRun));

    internal static IQueryable<InsightRow> Sessions(
        DiaryDbContext db, string owner, DateTimeOffset lower, DateTimeOffset upper) =>
        from run in OwnedCompleted(db, owner)
        join type in db.RunTypes.AsNoTracking() on run.RunTypeCode equals type.Code
        where run.StartAtUtc >= lower && run.StartAtUtc < upper
        select new InsightRow(run.StartAtUtc, run.RunTypeCode, type.IsRace, run.DistanceMeters,
            run.DurationSeconds, run.ElevationGainMeters, run.ElevationLossMeters, run.PerceivedEffort);

    internal static IQueryable<DateTimeOffset?> ActivityTimes(DiaryDbContext db, string owner, DateTimeOffset upper) =>
        from run in OwnedCompleted(db, owner)
        join type in db.RunTypes.AsNoTracking() on run.RunTypeCode equals type.Code
        where run.StartAtUtc < upper && run.RunTypeCode != "rest-injury"
            && (type.IsRace || ActivityCodes.Contains(run.RunTypeCode))
        select (DateTimeOffset?)run.StartAtUtc;

    internal static IQueryable<RaceHistoryItem> RacePage(
        DiaryDbContext db, string owner, DateTimeOffset upper, DateTimeOffset? afterTime, Guid? afterId, int take)
    {
        var races = from run in OwnedCompleted(db, owner)
                    join type in db.RunTypes.AsNoTracking() on run.RunTypeCode equals type.Code
                    where type.IsRace && run.StartAtUtc < upper
                    select run;
        if (afterTime is { } timestamp && afterId is { } id)
            races = races.Where(run => run.StartAtUtc < timestamp
                || run.StartAtUtc == timestamp && run.Id.CompareTo(id) < 0);

        return races.OrderByDescending(run => run.StartAtUtc).ThenByDescending(run => run.Id)
            .Select(run => new RaceHistoryItem(run.Id, run.StartAtUtc, run.RunTypeCode,
                run.DistanceMeters, run.DurationSeconds,
                run.RaceDetails == null ? null : run.RaceDetails.RaceName,
                run.RaceDetails == null ? null : run.RaceDetails.OfficialDistanceCategory,
                run.RaceDetails == null ? null : run.RaceDetails.ResultTimeSeconds,
                run.RaceDetails == null ? null : run.RaceDetails.OverallPlace,
                run.RaceDetails == null ? null : run.RaceDetails.CategoryPlace))
            .Take(take);
    }
}

internal sealed record InsightRow(
    DateTimeOffset StartAtUtc, string RunTypeCode, bool IsRace, double? DistanceMeters,
    int? DurationSeconds, double? ElevationGainMeters, double? ElevationLossMeters, int? PerceivedEffort);
