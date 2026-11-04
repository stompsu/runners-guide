using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Domain;

namespace RunnersDiary.Infrastructure;

public sealed class DemoDataSeeder(DiaryDbContext db, TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DiaryUser> CreateGuestAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var user = new DiaryUser
        {
            Id = Guid.NewGuid().ToString("N"),
            IsGuest = true,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(24),
            DisplayName = "Demo runner"
        };
        db.Users.Add(user);

        await SeedRunTypesAsync(cancellationToken);
        var template = await LoadTemplateAsync(cancellationToken);
        var runs = CreateRuns(user.Id, now, template);
        db.Runs.AddRange(runs);
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task SeedRunTypesAsync(CancellationToken cancellationToken)
    {
        foreach (var runType in RunTypeCatalog.All)
        {
            if (!await db.RunTypes.AnyAsync(type => type.Code == runType.Code, cancellationToken))
            {
                db.RunTypes.Add(new RunType
                {
                    Code = runType.Code,
                    Name = runType.Name,
                    Category = runType.Category,
                    IsRace = runType.IsRace
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<DemoTemplate> LoadTemplateAsync(CancellationToken cancellationToken)
    {
        await using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("RunnersDiary.Infrastructure.Demo.demo-runs.json")
            ?? throw new InvalidOperationException("The embedded demo run template is missing.");

        return await JsonSerializer.DeserializeAsync<DemoTemplate>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The embedded demo run template is invalid.");
    }

    private static List<Run> CreateRuns(string userId, DateTimeOffset now, DemoTemplate template)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var runs = new List<Run>();
        var monday = StartOfWeek(today);

        for (var week = 25; week >= 0; week--)
        {
            foreach (var item in template.WeeklySchedule)
            {
                var day = monday.AddDays(-week * 7 + item.Weekday);
                if (day > today)
                {
                    continue;
                }

                var taper = week is 3 or 11 or 19;
                var loadSpike = week is 4 or 12 or 20;
                var runTypeCode = item.RunTypeCodes[(25 - week) % item.RunTypeCodes.Count];
                double? distanceMeters = runTypeCode == "cross-training" ? null : item.DistanceMeters;
                runs.Add(CreateRun(
                    userId,
                    day,
                    runTypeCode,
                    item.Title,
                    distanceMeters is null ? null : distanceMeters * (taper ? 0.65 : loadSpike ? 1.35 : 1),
                    Convert.ToInt32(item.DurationSeconds * (taper ? 0.75 : loadSpike ? 1.3 : 1)),
                    item.PerceivedEffort,
                    RunStatus.Completed,
                    now,
                    surface: runTypeCode == "cross-training" ? "Gym" : null));
            }

            if (week is 4 or 12 or 20)
            {
                var day = monday.AddDays(-week * 7 + 2);
                if (day <= today)
                {
                    runs.Add(CreateRun(userId, day, "intervals", "Load-spike interval session", 9000, 3300, 9, RunStatus.Completed, now));
                }
            }
        }

        foreach (var race in template.CompletedRaces)
        {
            runs.Add(CreateRun(
                userId,
                today.AddDays(-race.DaysAgo),
                race.RunTypeCode,
                race.Title,
                race.DistanceMeters,
                race.DurationSeconds,
                race.PerceivedEffort,
                RunStatus.Completed,
                now,
                race.RaceName,
                race.DistanceCategory,
                race.Stars,
                race.Comment));
        }

        foreach (var race in template.PlannedRaces)
        {
            runs.Add(CreateRun(
                userId,
                today.AddDays(race.DaysAhead),
                race.RunTypeCode,
                race.Title,
                race.DistanceMeters,
                null,
                null,
                RunStatus.Planned,
                now,
                race.RaceName,
                race.DistanceCategory));
        }

        if (!runs.Any(run =>
                    DateOnly.FromDateTime(run.StartAtUtc.UtcDateTime).Year == today.Year
                    && DateOnly.FromDateTime(run.StartAtUtc.UtcDateTime).Month == today.Month))
        {
                runs.Add(CreateRun(
                    userId,
                    today,
                    "easy-run",
                    "Planned run this month",
                    5000,
                    1500,
                    null,
                    RunStatus.Planned,
                    now));
        }

        return runs;
    }

    private static DateOnly StartOfWeek(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static Run CreateRun(
        string userId,
        DateOnly date,
        string type,
        string title,
        double? distanceMeters,
        int? durationSeconds,
        int? rpe,
        RunStatus status,
        DateTimeOffset now,
        string? raceName = null,
        string? distanceCategory = null,
        int? stars = null,
        string? comment = null,
        string? surface = null)
    {
        var isRace = raceName is not null;
        var run = new Run
        {
            UserId = userId,
            StartAtUtc = new DateTimeOffset(date.ToDateTime(new TimeOnly(7, 0), DateTimeKind.Utc)),
            Status = status,
            RunTypeCode = type,
            Title = title,
            DistanceMeters = distanceMeters,
            DurationSeconds = durationSeconds,
            ElevationGainMeters = type is "trail-training" or "trail-race" ? 280 : 60,
            ElevationLossMeters = type is "trail-training" or "trail-race" ? 260 : 55,
            AverageHeartRateBpm = status == RunStatus.Completed ? 145 : null,
            MaximumHeartRateBpm = status == RunStatus.Completed ? 172 : null,
            PerceivedEffort = rpe,
            Surface = surface ?? (type is "trail-training" or "trail-race" ? "Trail" : "Road"),
            Location = new RunLocation
            {
                DisplayName = "Gliwice, Poland",
                Coordinates = new NetTopologySuite.Geometries.Point(18.6714, 50.2945) { SRID = 4326 }
            },
            RaceDetails = isRace
                ? new RaceDetails
                {
                    RaceName = raceName,
                    OfficialDistanceCategory = distanceCategory
                }
                : null,
            Rating = stars is null ? null : new RunRating { Stars = stars.Value, Comment = comment }
        };

        return run;
    }

    private sealed class DemoTemplate
    {
        public required List<WeeklyRunTemplate> WeeklySchedule { get; init; }
        public required List<CompletedRaceTemplate> CompletedRaces { get; init; }
        public required List<PlannedRaceTemplate> PlannedRaces { get; init; }
    }

    private sealed class WeeklyRunTemplate
    {
        public int Weekday { get; init; }
        public required List<string> RunTypeCodes { get; init; }
        public required string Title { get; init; }
        public double DistanceMeters { get; init; }
        public int DurationSeconds { get; init; }
        public int PerceivedEffort { get; init; }
    }

    private sealed class CompletedRaceTemplate
    {
        public int DaysAgo { get; init; }
        public required string RunTypeCode { get; init; }
        public required string Title { get; init; }
        public required string RaceName { get; init; }
        public required string DistanceCategory { get; init; }
        public double DistanceMeters { get; init; }
        public int DurationSeconds { get; init; }
        public int PerceivedEffort { get; init; }
        public int Stars { get; init; }
        public required string Comment { get; init; }
    }

    private sealed class PlannedRaceTemplate
    {
        public int DaysAhead { get; init; }
        public required string RunTypeCode { get; init; }
        public required string Title { get; init; }
        public required string RaceName { get; init; }
        public required string DistanceCategory { get; init; }
        public double DistanceMeters { get; init; }
    }
}
