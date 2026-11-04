using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Infrastructure;

namespace RunnersDiary.Api.Endpoints;

public static class InsightsEndpoints
{
    public static void MapInsightsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/insights").RequireAuthorization().WithTags("Insights");
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });
        group.MapGet("", GetAsync)
            .WithSummary("Completed activity insights for the caller, using inclusive UTC dates.")
            .WithDescription("from/to: YYYY-MM-DD, year >= 2, no future dates, at most 367 inclusive days. "
                + "bucket: week or month. Weeks start Monday (ISO). Empty days/buckets are explicit. "
                + "KnownSubtotal never estimates missing values; inspect coverage. Load is minutes × RPE. "
                + "loadSeries includes every selected date with trailing 7/28-day coverage, chronic weekly load, "
                + "ACWR, band/reason and population-SD monotony; lookback includes 27 days before from. "
                + "trainingLoad is the same calculation anchored to to. "
                + "Easy pace is paired, distance-weighted seconds/km. Comparisons are calendar-period-to-date.")
            .Produces<InsightsResponse>().ProducesValidationProblem().Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/races", RacesAsync)
            .WithSummary("Completed race history owned by the caller, including races without details.")
            .WithDescription("to: inclusive UTC YYYY-MM-DD (not future). pageSize defaults to 25, range 1–100. "
                + "Opaque owner/to-bound cursor; newest UTC start then descending ID. No snapshot isolation across pages.")
            .Produces<RaceHistoryResponse>().ProducesValidationProblem().Produces(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> GetAsync(
        string from, string to, string bucket, ClaimsPrincipal principal, DiaryDbContext db,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var user = await RunEndpoints.GetCurrentUserAsync(principal, db, now, cancellationToken);
        if (user is null) return Results.Unauthorized();
        if (!TryDate(from, now, out var start) || !TryDate(to, now, out var end)
            || start > end || end.DayNumber - start.DayNumber > 366
            || bucket is not ("week" or "month"))
        {
            return Invalid("query", "Use non-future YYYY-MM-DD dates (year >= 2), from <= to, at most 367 inclusive days, and bucket=week|month.");
        }

        var required = InsightsCalculator.RequiredFrom(start, end);
        var lower = Midnight(required);
        var upper = Midnight(end.AddDays(1));
        // Only scalar fields cross the persistence boundary. Grouping and numerical rules are pure
        // domain code, identical on SQL Server and InMemory; no entity graphs or per-chart queries.
        var rows = await InsightsQueries.Sessions(db, user.Id, lower, upper).ToListAsync(cancellationToken);
        var first = await InsightsQueries.ActivityTimes(db, user.Id, upper).MinAsync(cancellationToken);
        var sessions = rows.Select(row => new InsightSession(DateOnly.FromDateTime(row.StartAtUtc.UtcDateTime),
            row.RunTypeCode, row.IsRace, row.DistanceMeters, row.DurationSeconds, row.ElevationGainMeters,
            row.ElevationLossMeters, row.PerceivedEffort)).ToArray();
        return Results.Ok(InsightsCalculator.Calculate(sessions, start, end,
            bucket == "week" ? InsightBucket.Week : InsightBucket.Month,
            first is null ? null : DateOnly.FromDateTime(first.Value.UtcDateTime)));
    }

    private static async Task<IResult> RacesAsync(
        string to, int? pageSize, string? cursor, ClaimsPrincipal principal, DiaryDbContext db,
        TimeProvider timeProvider, IDataProtectionProvider protection, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var user = await RunEndpoints.GetCurrentUserAsync(principal, db, now, cancellationToken);
        if (user is null) return Results.Unauthorized();
        var size = pageSize ?? 25;
        if (!TryDate(to, now, out var end) || size is < 1 or > 100)
            return Invalid("query", "Use a non-future YYYY-MM-DD to date (year >= 2) and pageSize from 1 to 100.");

        var protector = protection.CreateProtector("RunnersDiary.Insights.RaceCursor.v1", user.Id);
        RaceCursor? after = null;
        if (cursor is not null)
        {
            if (cursor.Length is 0 or > 2048) return Invalid("cursor", "Invalid cursor.");
            try
            {
                after = JsonSerializer.Deserialize<RaceCursor>(protector.Unprotect(cursor));
                if (after is null || after.To != end || after.StartAtUtc >= Midnight(end.AddDays(1))
                    || after.Id == Guid.Empty)
                    return Invalid("cursor", "Invalid cursor.");
            }
            catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
            {
                return Invalid("cursor", "Invalid cursor.");
            }
        }

        var upper = Midnight(end.AddDays(1));
        var items = await InsightsQueries.RacePage(db, user.Id, upper, after?.StartAtUtc, after?.Id, size + 1)
            .ToListAsync(cancellationToken);
        string? next = null;
        if (items.Count > size)
        {
            items.RemoveAt(size);
            var last = items[^1];
            next = protector.Protect(JsonSerializer.Serialize(new RaceCursor(end, last.StartAtUtc, last.Id)));
        }
        return Results.Ok(new RaceHistoryResponse(items, next));
    }

    private static bool TryDate(string? value, DateTimeOffset now, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
        && date.Year >= 2 && date <= DateOnly.FromDateTime(now.UtcDateTime) && date < DateOnly.MaxValue;

    private static DateTimeOffset Midnight(DateOnly day) =>
        new(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    private static IResult Invalid(string key, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });

    private sealed record RaceCursor(DateOnly To, DateTimeOffset StartAtUtc, Guid Id);
}

public sealed record RaceHistoryItem(
    Guid Id, DateTimeOffset StartAtUtc, string RunTypeCode, double? DistanceMeters, int? DurationSeconds,
    string? RaceName, string? OfficialDistanceCategory, int? ResultTimeSeconds,
    int? OverallPlace, int? CategoryPlace);
public sealed record RaceHistoryResponse(IReadOnlyList<RaceHistoryItem> Items, string? NextCursor);
