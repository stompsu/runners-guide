using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using RunnersDiary.Domain;
using RunnersDiary.Infrastructure;

namespace RunnersDiary.Api.Endpoints;

public static class RunEndpoints
{
    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 4326);

    public static void MapRunEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var runs = endpoints.MapGroup("/api/runs")
            .RequireAuthorization()
            .WithTags("Runs");

        runs.MapPost("", CreateAsync)
            .WithSummary("Create a run owned by the authenticated user.")
            .Produces<RunResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);
        runs.MapGet("", ListAsync)
            .WithSummary("List the authenticated user's runs within inclusive UTC dates.")
            .Produces<RunResponse[]>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);
        runs.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Get one of the authenticated user's runs.")
            .Produces<RunResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
        runs.MapPut("/{id:guid}", UpdateAsync)
            .WithSummary("Replace one of the authenticated user's runs.")
            .Produces<RunResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
        runs.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Delete one of the authenticated user's runs.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> CreateAsync(
        RunInput input,
        ClaimsPrincipal principal,
        DiaryDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var type = await db.RunTypes.SingleOrDefaultAsync(
            item => item.Code == input.RunTypeCode,
            cancellationToken);
        var run = CreateRun(input, string.Empty);
        var errors = type is null
            ? new Dictionary<string, string[]> { [nameof(input.RunTypeCode)] = ["Unknown run type."] }
            : RunValidation.Validate(run, type.IsRace);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var user = await GetCurrentUserAsync(principal, db, timeProvider, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        run.UserId = user.Id;
        db.Runs.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/runs/{run.Id}", ToResponse(run, type!));
    }

    private static async Task<IResult> ListAsync(
        DateOnly from,
        DateOnly to,
        ClaimsPrincipal principal,
        DiaryDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (from > to || to == DateOnly.MaxValue || to.DayNumber - from.DayNumber > 366)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["dateRange"] = ["Provide inclusive from and to dates spanning no more than 367 calendar days."]
            });
        }

        var user = await GetCurrentUserAsync(principal, db, timeProvider, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var endExclusive = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var runs = await db.Runs.AsNoTracking()
            .Where(run => run.UserId == user.Id && run.StartAtUtc >= start && run.StartAtUtc < endExclusive)
            .OrderBy(run => run.StartAtUtc)
            .ToListAsync(cancellationToken);
        var types = await db.RunTypes.AsNoTracking().ToDictionaryAsync(type => type.Code, cancellationToken);
        return Results.Ok(runs.Select(run => ToResponse(run, types[run.RunTypeCode])));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        ClaimsPrincipal principal,
        DiaryDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(principal, db, timeProvider, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var run = await db.Runs.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id && item.UserId == user.Id, cancellationToken);
        if (run is null)
        {
            return Results.NotFound();
        }

        var type = await db.RunTypes.AsNoTracking()
            .SingleAsync(item => item.Code == run.RunTypeCode, cancellationToken);
        return Results.Ok(ToResponse(run, type));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        RunInput input,
        ClaimsPrincipal principal,
        DiaryDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(principal, db, timeProvider, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var run = await db.Runs.SingleOrDefaultAsync(
            item => item.Id == id && item.UserId == user.Id,
            cancellationToken);
        if (run is null)
        {
            return Results.NotFound();
        }

        var type = await db.RunTypes.SingleOrDefaultAsync(
            item => item.Code == input.RunTypeCode,
            cancellationToken);
        var candidate = CreateRun(input, user.Id);
        candidate.Id = id;
        var errors = type is null
            ? new Dictionary<string, string[]> { [nameof(input.RunTypeCode)] = ["Unknown run type."] }
            : RunValidation.Validate(candidate, type.IsRace);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        CopyFields(candidate, run);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(run, type!));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ClaimsPrincipal principal,
        DiaryDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(principal, db, timeProvider, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var run = await db.Runs.SingleOrDefaultAsync(
            item => item.Id == id && item.UserId == user.Id,
            cancellationToken);
        if (run is null)
        {
            return Results.NotFound();
        }

        db.Runs.Remove(run);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<DiaryUser?> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        DiaryDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var id = principal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(id) || id.Length > 200)
        {
            return null;
        }

        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (principal.IsInRole("guest"))
        {
            return user is { IsGuest: true, ExpiresAtUtc: not null }
                && user.ExpiresAtUtc > timeProvider.GetUtcNow()
                ? user
                : null;
        }

        if (user is not null)
        {
            return user.IsGuest ? null : user;
        }

        var displayName = principal.FindFirstValue("name") ?? principal.FindFirstValue("preferred_username");
        if (displayName?.Length > 200)
        {
            displayName = displayName[..200];
        }

        user = new DiaryUser
        {
            Id = id,
            IsGuest = false,
            CreatedAtUtc = timeProvider.GetUtcNow(),
            DisplayName = displayName
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    private static Run CreateRun(RunInput input, string userId) => new()
    {
        UserId = userId,
        StartAtUtc = input.StartAtUtc.ToUniversalTime(),
        Status = input.Status,
        RunTypeCode = input.RunTypeCode,
        Title = input.Title,
        Notes = input.Notes,
        DistanceMeters = input.DistanceMeters,
        DurationSeconds = input.DurationSeconds,
        ElevationGainMeters = input.ElevationGainMeters,
        ElevationLossMeters = input.ElevationLossMeters,
        AverageHeartRateBpm = input.AverageHeartRateBpm,
        MaximumHeartRateBpm = input.MaximumHeartRateBpm,
        PerceivedEffort = input.PerceivedEffort,
        ShoesOrGear = input.ShoesOrGear,
        Weather = input.Weather,
        Surface = input.Surface,
        Location = input.Location is null
            ? null
            : new RunLocation
            {
                DisplayName = input.Location.DisplayName,
                Coordinates = GeometryFactory.CreatePoint(new Coordinate(input.Location.Longitude, input.Location.Latitude))
            },
        RaceDetails = input.RaceDetails is null
            ? null
            : new RaceDetails
            {
                RaceName = input.RaceDetails.RaceName,
                OfficialDistanceCategory = input.RaceDetails.OfficialDistanceCategory,
                BibNumber = input.RaceDetails.BibNumber,
                TargetTimeSeconds = input.RaceDetails.TargetTimeSeconds,
                ResultTimeSeconds = input.RaceDetails.ResultTimeSeconds,
                OverallPlace = input.RaceDetails.OverallPlace,
                CategoryPlace = input.RaceDetails.CategoryPlace,
                ResultsUrl = input.RaceDetails.ResultsUrl
            },
        Rating = input.Rating is null
            ? null
            : new RunRating { Stars = input.Rating.Stars, Comment = input.Rating.Comment }
    };

    private static void CopyFields(Run source, Run destination)
    {
        destination.StartAtUtc = source.StartAtUtc;
        destination.Status = source.Status;
        destination.RunTypeCode = source.RunTypeCode;
        destination.Title = source.Title;
        destination.Notes = source.Notes;
        destination.DistanceMeters = source.DistanceMeters;
        destination.DurationSeconds = source.DurationSeconds;
        destination.ElevationGainMeters = source.ElevationGainMeters;
        destination.ElevationLossMeters = source.ElevationLossMeters;
        destination.AverageHeartRateBpm = source.AverageHeartRateBpm;
        destination.MaximumHeartRateBpm = source.MaximumHeartRateBpm;
        destination.PerceivedEffort = source.PerceivedEffort;
        destination.ShoesOrGear = source.ShoesOrGear;
        destination.Weather = source.Weather;
        destination.Surface = source.Surface;
        destination.Location = source.Location;
        destination.RaceDetails = source.RaceDetails;
        destination.Rating = source.Rating;
    }

    private static RunResponse ToResponse(Run run, RunType type) => new(
        run.Id,
        run.StartAtUtc,
        run.Status,
        run.RunTypeCode,
        type.Name,
        run.Title,
        run.Notes,
        run.DistanceMeters,
        run.DurationSeconds,
        run.ElevationGainMeters,
        run.ElevationLossMeters,
        run.AverageHeartRateBpm,
        run.MaximumHeartRateBpm,
        run.PerceivedEffort,
        run.ShoesOrGear,
        run.Weather,
        run.Surface,
        run.Location is null
            ? null
            : new LocationResponse(
                run.Location.DisplayName,
                run.Location.Coordinates.Y,
                run.Location.Coordinates.X),
        run.RaceDetails is null
            ? null
            : new RaceDetailsResponse(
                run.RaceDetails.RaceName,
                run.RaceDetails.OfficialDistanceCategory,
                run.RaceDetails.BibNumber,
                run.RaceDetails.TargetTimeSeconds,
                run.RaceDetails.ResultTimeSeconds,
                run.RaceDetails.OverallPlace,
                run.RaceDetails.CategoryPlace,
                run.RaceDetails.ResultsUrl),
        run.Rating is null ? null : new RatingResponse(run.Rating.Stars, run.Rating.Comment));
}

public sealed record RunInput(
    DateTimeOffset StartAtUtc,
    RunStatus Status,
    string RunTypeCode,
    string Title,
    string? Notes = null,
    double? DistanceMeters = null,
    int? DurationSeconds = null,
    double? ElevationGainMeters = null,
    double? ElevationLossMeters = null,
    int? AverageHeartRateBpm = null,
    int? MaximumHeartRateBpm = null,
    int? PerceivedEffort = null,
    string? ShoesOrGear = null,
    string? Weather = null,
    string? Surface = null,
    LocationInput? Location = null,
    RaceDetailsInput? RaceDetails = null,
    RatingInput? Rating = null);

public sealed record LocationInput(string DisplayName, double Latitude, double Longitude);

public sealed record RaceDetailsInput(
    string? RaceName = null,
    string? OfficialDistanceCategory = null,
    string? BibNumber = null,
    int? TargetTimeSeconds = null,
    int? ResultTimeSeconds = null,
    int? OverallPlace = null,
    int? CategoryPlace = null,
    string? ResultsUrl = null);

public sealed record RatingInput(int Stars, string? Comment = null);

public sealed record RunResponse(
    Guid Id,
    DateTimeOffset StartAtUtc,
    RunStatus Status,
    string RunTypeCode,
    string RunTypeName,
    string Title,
    string? Notes,
    double? DistanceMeters,
    int? DurationSeconds,
    double? ElevationGainMeters,
    double? ElevationLossMeters,
    int? AverageHeartRateBpm,
    int? MaximumHeartRateBpm,
    int? PerceivedEffort,
    string? ShoesOrGear,
    string? Weather,
    string? Surface,
    LocationResponse? Location,
    RaceDetailsResponse? RaceDetails,
    RatingResponse? Rating);

public sealed record LocationResponse(string DisplayName, double Latitude, double Longitude);

public sealed record RaceDetailsResponse(
    string? RaceName,
    string? OfficialDistanceCategory,
    string? BibNumber,
    int? TargetTimeSeconds,
    int? ResultTimeSeconds,
    int? OverallPlace,
    int? CategoryPlace,
    string? ResultsUrl);

public sealed record RatingResponse(int Stars, string? Comment);
