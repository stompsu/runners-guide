using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using RunnersDiary.Api.Authentication;
using RunnersDiary.Api.Endpoints;
using RunnersDiary.Domain;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Infrastructure;

public sealed class InsightsApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private const string Query = "/api/insights?from=2026-10-26&to=2026-11-04&bucket=week";

    [Theory]
    [InlineData("/api/insights?from=2026-10-26&to=2026-11-04&bucket=week")]
    [InlineData("/api/insights/races?to=2026-11-04")]
    public async Task Authentication_is_required(string url)
    {
        using var factory = new RunApiTests.DiaryApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("?from=2026-11-04&to=2026-11-03&bucket=week")]
    [InlineData("?from=2026-11-01&to=2026-11-05&bucket=week")]
    [InlineData("?from=2025-11-01&to=2026-11-04&bucket=week")]
    [InlineData("?from=2026-02-30&to=2026-11-04&bucket=week")]
    [InlineData("?from=2026-1-01&to=2026-11-04&bucket=week")]
    [InlineData("?from=0001-01-01&to=0001-01-02&bucket=week")]
    [InlineData("?from=2026-11-01&to=2026-11-04&bucket=day")]
    [InlineData("/races?to=2026-11-05")]
    [InlineData("/races?to=2026-11-04&pageSize=0")]
    [InlineData("/races?to=2026-11-04&pageSize=101")]
    [InlineData("/races?to=2026-11-04&cursor=garbage")]
    public async Task Invalid_queries_are_rejected(string query)
    {
        using var root = new RunApiTests.DiaryApiFactory();
        using var factory = WithClock(root);
        using var client = factory.CreateClient();
        Authenticate(client, factory.Services, "owner", guest: false);
        using var response = await client.GetAsync("/api/insights" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Theory]
    [InlineData("?to=2026-11-04&bucket=week")]
    [InlineData("?from=2026-11-01&bucket=week")]
    [InlineData("?from=2026-11-01&to=2026-11-04")]
    [InlineData("/races")]
    public async Task Required_query_bindings_reject_missing_parameters(string query)
    {
        using var root = new RunApiTests.DiaryApiFactory();
        using var factory = WithClock(root);
        using var client = factory.CreateClient();
        Authenticate(client, factory.Services, "owner", guest: false);
        using var response = await client.GetAsync("/api/insights" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Maximum_inclusive_range_is_367_days_and_empty_buckets_are_returned()
    {
        using var root = new RunApiTests.DiaryApiFactory();
        using var factory = WithClock(root);
        using var client = factory.CreateClient();
        Authenticate(client, factory.Services, "new-user", guest: false);
        using var response = await client.GetAsync("/api/insights?from=2025-11-03&to=2026-11-04&bucket=month");
        response.EnsureSuccessStatusCode();
        var insights = await response.Content.ReadFromJsonAsync<InsightsResponse>(JsonOptions);
        Assert.Equal(367, insights!.Daily.Count);
        Assert.Equal(367, insights.LoadSeries.Count);
        Assert.All(insights.LoadSeries, point =>
        {
            Assert.Equal(0, point.Acute7.KnownSubtotal);
            Assert.Equal(UnavailableReason.ZeroChronicLoad, point.Acwr.Reason);
            Assert.Equal(UnavailableReason.NoLoad, point.Monotony.Reason);
        });
        Assert.Equal(13, insights.Buckets.Count);
        Assert.All(insights.Buckets, bucket => Assert.Equal(0, bucket.Period.Totals.SessionCount));
    }

    [Fact]
    public async Task Load_series_fetches_27_days_before_selected_start_even_across_year_boundary()
    {
        using var root = new RunApiTests.DiaryApiFactory();
        using var factory = WithClock(root);
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services, false);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiaryDbContext>();
            foreach (var (date, seconds) in new[]
            {
                (new DateTimeOffset(2025, 12, 6, 12, 0, 0, TimeSpan.Zero), 600),
                (new DateTimeOffset(2025, 12, 7, 12, 0, 0, TimeSpan.Zero), 60),
                (new DateTimeOffset(2026, 1, 3, 12, 0, 0, TimeSpan.Zero), 120)
            })
            {
                var run = Run("owner");
                run.StartAtUtc = date;
                run.DurationSeconds = seconds;
                run.PerceivedEffort = 1;
                db.Runs.Add(run);
            }
            await db.SaveChangesAsync();
        }
        Authenticate(client, factory.Services, "owner", false);
        var result = (await client.GetFromJsonAsync<InsightsResponse>(
            "/api/insights?from=2026-01-03&to=2026-02-01&bucket=week", JsonOptions))!;
        Assert.Equal(30, result.LoadSeries.Count);
        var first = result.LoadSeries[0];
        Assert.Equal(new DateOnly(2026, 1, 3), first.Date);
        Assert.Equal(new MetricCoverage(2, 1, 1), first.Acute7);
        Assert.Equal(new MetricCoverage(3, 2, 2), first.Chronic28);
        Assert.Equal(0.75m, first.ChronicWeeklyKnownSubtotal);
        Assert.Equal(8d / 3, first.Acwr.Value!.Value, 12);
        Assert.Equal(1 / Math.Sqrt(6), first.Monotony.Value!.Value, 12);
        Assert.Equal(LoadBand.High, first.Band);
        Assert.Equal(new MetricCoverage(2, 1, 1), result.LoadSeries[1].Chronic28);
        Assert.Equal(result.TrainingLoad.Acwr, result.LoadSeries[^1].Acwr);
        Assert.Equal(1, result.Selected.Totals.SessionCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Owner_filter_planned_rest_pending_delete_gaps_and_missing_values_are_respected(bool guest)
    {
        using var root = new RunApiTests.DiaryApiFactory();
        using var factory = WithClock(root);
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services, guest);
        Authenticate(client, factory.Services, "owner", guest);
        using var response = await client.GetAsync(Query);
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl!.NoStore);
        var result = (await response.Content.ReadFromJsonAsync<InsightsResponse>(JsonOptions))!;
        Assert.Equal(3, result.Selected.Totals.SessionCount);
        Assert.Equal(1, result.Selected.Totals.RestSessionCount);
        Assert.Equal(1, result.YearToDate.Totals.RestSessionCount);
        Assert.Equal(1, result.Daily.Sum(day => day.Totals.RestSessionCount));
        Assert.Equal(1, result.Buckets.Sum(bucket => bucket.Period.Totals.RestSessionCount));
        Assert.DoesNotContain(result.Distribution, type => type.TypeCode == "rest-injury");
        Assert.Equal(1d, result.Distribution.Sum(type => type.Share));
        Assert.Equal(2000, result.Selected.Totals.RunningDistanceMeters.KnownSubtotal);
        Assert.Equal(2, result.Selected.Totals.RunningDistanceMeters.EligibleCount);
        Assert.Equal(900, result.Selected.Totals.DurationSeconds.KnownSubtotal);
        Assert.Equal(1, result.Selected.Totals.Load.MissingCount);
        Assert.Equal(UnavailableReason.MissingData, result.TrainingLoad.Acwr.Reason);
        Assert.Equal(10, result.Daily.Count);
        Assert.Equal(2, result.Buckets.Count);
        Assert.Equal(0, result.Daily[0].Totals.SessionCount);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("location", body, StringComparison.OrdinalIgnoreCase);

        using var other = factory.CreateClient();
        Authenticate(other, factory.Services, "other", guest);
        var isolated = (await other.GetFromJsonAsync<InsightsResponse>(Query, JsonOptions))!;
        Assert.Equal(99999, isolated.Selected.Totals.RunningDistanceMeters.KnownSubtotal);
        Assert.Equal(1, isolated.Selected.Totals.SessionCount);
        Assert.Equal(0, isolated.Selected.Totals.RestSessionCount);
    }

    [Fact]
    public async Task Expired_guest_cannot_read_insights_or_races()
    {
        using var root = new RunApiTests.DiaryApiFactory();
        using var factory = WithClock(root);
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services, true);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiaryDbContext>();
            (await db.Users.SingleAsync(u => u.Id == "owner")).ExpiresAtUtc = Clock.Now.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        Authenticate(client, factory.Services, "owner", true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Query)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/insights/races?to=2026-11-04")).StatusCode);
    }

    [Fact]
    public async Task Race_keyset_is_stable_for_tied_times_missing_details_and_owner_bound()
    {
        using var root = new RunApiTests.DiaryApiFactory();
        using var factory = WithClock(root);
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services, false);
        var expected = new List<Guid>();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiaryDbContext>();
            for (var i = 1; i <= 5; i++)
            {
                var run = Run("owner", "road-race");
                run.Id = new Guid(i, 0, 0, new byte[8]);
                if (i == 3) run.RaceDetails = new RaceDetails { RaceName = "Race", ResultTimeSeconds = 1200 };
                expected.Add(run.Id);
                db.Runs.Add(run);
            }
            db.Runs.Add(Run("other", "road-race"));
            db.Runs.Add(Run("owner", "road-race", planned: true));
            var deleted = Run("owner", "road-race");
            deleted.GpxTrack = Track(true);
            db.Runs.Add(deleted);
            await db.SaveChangesAsync();
        }
        Authenticate(client, factory.Services, "owner", false);
        const string url = "/api/insights/races?to=2026-11-04&pageSize=2";
        using var response = await client.GetAsync(url);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var first = (await response.Content.ReadFromJsonAsync<RaceHistoryResponse>())!;
        Assert.Equal(expected.OrderDescending().Take(2), first.Items.Select(i => i.Id));
        Assert.All(first.Items, item => Assert.Null(item.RaceName));
        Assert.NotNull(first.NextCursor);
        var repeat = (await client.GetFromJsonAsync<RaceHistoryResponse>(url))!;
        Assert.Equal(first.Items, repeat.Items);
        var second = (await client.GetFromJsonAsync<RaceHistoryResponse>(
            url + "&cursor=" + Uri.EscapeDataString(first.NextCursor)))!;
        var third = (await client.GetFromJsonAsync<RaceHistoryResponse>(
            url + "&cursor=" + Uri.EscapeDataString(second.NextCursor!)))!;
        Assert.Null(third.NextCursor);
        Assert.Equal(expected.OrderDescending(), first.Items.Concat(second.Items).Concat(third.Items).Select(i => i.Id));
        Assert.Equal("Race", second.Items[0].RaceName);

        Authenticate(client, factory.Services, "other", false);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(
            url + "&cursor=" + Uri.EscapeDataString(first.NextCursor))).StatusCode);
        Authenticate(client, factory.Services, "owner", false);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(
            "/api/insights/races?to=2026-11-03&cursor=" + Uri.EscapeDataString(first.NextCursor))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url + "&cursor=" + new string('a', 2049))).StatusCode);
    }

    [Fact]
    public async Task Utc_date_and_single_clock_capture_are_used()
    {
        var clock = new Clock();
        using var root = new RunApiTests.DiaryApiFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services, false);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiaryDbContext>();
            var run = Run("owner");
            run.StartAtUtc = new DateTimeOffset(2026, 11, 5, 1, 0, 0, TimeSpan.FromHours(2));
            db.Runs.Add(run);
            await db.SaveChangesAsync();
        }
        Authenticate(client, factory.Services, "owner", false);
        clock.Calls = 0;
        var result = (await client.GetFromJsonAsync<InsightsResponse>(Query, JsonOptions))!;
        Assert.Equal(1, clock.Calls);
        Assert.Equal(4, result.Selected.Totals.SessionCount);
    }

    [Fact]
    public async Task Openapi_exposes_typed_insight_and_race_contracts()
    {
        using var factory = new RunApiTests.DiaryApiFactory();
        using var client = factory.CreateClient();
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = doc.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/insights", out _));
        Assert.True(paths.TryGetProperty("/api/insights/races", out _));
        foreach (var name in new[] { "from", "to", "bucket" })
        {
            var parameter = paths.GetProperty("/api/insights").GetProperty("get")
                .GetProperty("parameters").EnumerateArray()
                .Single(p => p.GetProperty("name").GetString() == name);
            Assert.True(parameter.GetProperty("required").GetBoolean());
        }
        var raceParameters = paths.GetProperty("/api/insights/races").GetProperty("get")
            .GetProperty("parameters").EnumerateArray().ToArray();
        Assert.True(raceParameters.Single(p => p.GetProperty("name").GetString() == "to")
            .GetProperty("required").GetBoolean());
        foreach (var name in new[] { "pageSize", "cursor" })
        {
            var parameter = raceParameters.Single(p => p.GetProperty("name").GetString() == name);
            Assert.False(parameter.TryGetProperty("required", out var required) && required.GetBoolean());
        }
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("InsightsResponse", out _));
        Assert.True(schemas.GetProperty("InsightsResponse").GetProperty("properties")
            .TryGetProperty("loadSeries", out _));
        var loadPoint = schemas.GetProperty("LoadSeriesPoint").GetProperty("properties");
        foreach (var name in new[] { "date", "acute7", "chronic28", "chronicWeeklyKnownSubtotal",
            "acwr", "band", "bandReason", "monotony" })
            Assert.True(loadPoint.TryGetProperty(name, out _));
        Assert.True(schemas.TryGetProperty("MetricCoverage", out _));
        Assert.True(schemas.GetProperty("InsightTotals").GetProperty("properties")
            .TryGetProperty("restSessionCount", out var restCount));
        var restCountType = restCount.GetProperty("type");
        Assert.True(restCountType.ValueKind == JsonValueKind.String
            ? restCountType.GetString() == "integer"
            : restCountType.EnumerateArray().Any(type => type.GetString() == "integer"));
        Assert.True(schemas.TryGetProperty("RaceHistoryResponse", out _));
        Assert.True(schemas.TryGetProperty("UnavailableReason", out var reasons));
        Assert.Contains("missingData", reasons.ToString(), StringComparison.Ordinal);
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithClock(
        RunApiTests.DiaryApiFactory root) =>
        root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new Clock());
        }));

    private static void Authenticate(HttpClient client, IServiceProvider services, string owner, bool guest)
    {
        var settings = services.GetRequiredService<GuestTokenSettings>();
        List<Claim> claims = [new("sub", owner)];
        if (guest) claims.Add(new("role", "guest"));
        // Test a validated non-guest principal without contacting an external Entra authority.
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience, claims,
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddHours(1),
            new SigningCredentials(settings.SigningKey, SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }

    private static async Task SeedAsync(IServiceProvider services, bool guest)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiaryDbContext>();
        db.RunTypes.AddRange(RunTypeCatalog.All.Select(t => new RunType
            { Code = t.Code, Category = t.Category, IsRace = t.IsRace, Name = t.Name }));
        foreach (var owner in new[] { "owner", "other" })
            db.Users.Add(new DiaryUser { Id = owner, IsGuest = guest, ExpiresAtUtc = Clock.Now.AddDays(1) });
        db.Runs.Add(Run("owner"));
        var missing = Run("owner");
        missing.DurationSeconds = null;
        db.Runs.Add(missing);
        db.Runs.Add(Run("owner", "cross-training"));
        db.Runs.Add(Run("owner", "rest-injury"));
        db.Runs.Add(Run("owner", "rest-injury", planned: true));
        var deletedRest = Run("owner", "rest-injury");
        deletedRest.GpxTrack = Track(true);
        db.Runs.Add(deletedRest);
        db.Runs.Add(Run("owner", planned: true));
        var deleting = Run("owner");
        deleting.GpxTrack = Track(true);
        db.Runs.Add(deleting);
        // Track-only deletion must not hide the parent run.
        db.Runs.Local.First(r => r.RunTypeCode == "easy-run").GpxTrack = Track(false);
        var foreign = Run("other");
        foreign.DistanceMeters = 99999;
        db.Runs.Add(foreign);
        await db.SaveChangesAsync();
    }

    private static Run Run(string owner, string type = "easy-run", bool planned = false) => new()
    {
        UserId = owner, RunTypeCode = type, Title = "private title", Notes = "private notes",
        StartAtUtc = Clock.Now.AddHours(-1), Status = planned ? RunStatus.Planned : RunStatus.Completed,
        DistanceMeters = 1000, DurationSeconds = type == "cross-training" ? 300 : 600,
        PerceivedEffort = 5, ElevationGainMeters = 20
    };

    private static GpxTrack Track(bool parent) => new()
    {
        BlobName = "private", IsPendingDeletion = true, DeleteParentRun = parent
    };

    private sealed class Clock : TimeProvider
    {
        public static DateTimeOffset Now { get; } = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        public int Calls { get; set; }
        public override DateTimeOffset GetUtcNow() { Calls++; return Now; }
    }
}
