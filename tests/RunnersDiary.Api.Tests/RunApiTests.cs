using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RunnersDiary.Api.Endpoints;
using RunnersDiary.Domain;
using RunnersDiary.Infrastructure;

public sealed class RunApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Run_endpoints_reject_requests_without_a_valid_token()
    {
        using var factory = new DiaryApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/runs?from=2026-11-01&to=2026-11-30");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Guest_creation_returns_token_and_seeds_current_month_data()
    {
        using var factory = new DiaryApiFactory();
        await factory.SeedRunTypesAsync();
        using var client = factory.CreateClient();

        var session = await CreateGuestAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        using var response = await client.GetAsync(
            $"/api/runs?from={new DateOnly(today.Year, today.Month, 1):yyyy-MM-dd}&to={new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)):yyyy-MM-dd}");
        var runs = await response.Content.ReadFromJsonAsync<RunResponse[]>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(runs!);
        Assert.True(session.ExpiresAtUtc > DateTimeOffset.UtcNow.AddHours(23));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiaryDbContext>();
        var guest = await db.Users.SingleAsync(user => user.IsGuest);
        Assert.Equal(session.ExpiresAtUtc, guest.ExpiresAtUtc);
        Assert.True(await db.Runs.CountAsync(run => run.UserId == guest.Id) >= 100);
        Assert.Equal(4, await db.Runs.CountAsync(run => run.UserId == guest.Id && run.Rating != null));
    }

    [Fact]
    public async Task Guest_data_is_isolated_and_crud_is_scoped_to_the_owner()
    {
        using var factory = new DiaryApiFactory();
        await factory.SeedRunTypesAsync();
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        var firstGuest = await CreateGuestAsync(firstClient);
        var secondGuest = await CreateGuestAsync(secondClient);
        firstClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstGuest.AccessToken);
        secondClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secondGuest.AccessToken);

        var input = new RunInput(
            DateTimeOffset.UtcNow.AddHours(-1),
            RunStatus.Completed,
            "easy-run",
            "My private run",
            DistanceMeters: 5000,
            DurationSeconds: 1500,
            Rating: new RatingInput(4, "Felt good"));
        using var createResponse = await firstClient.PostAsJsonAsync("/api/runs", input);
        var created = await createResponse.Content.ReadFromJsonAsync<RunResponse>(JsonOptions);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);

        using var listResponse = await secondClient.GetAsync(
            $"/api/runs?from={DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}&to={DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}");
        var secondRuns = await listResponse.Content.ReadFromJsonAsync<RunResponse[]>(JsonOptions);
        Assert.DoesNotContain(secondRuns!, run => run.Id == created.Id);

        using var getResponse = await secondClient.GetAsync($"/api/runs/{created.Id}");
        using var updateResponse = await secondClient.PutAsJsonAsync($"/api/runs/{created.Id}", input with { Title = "Stolen" });
        using var deleteResponse = await secondClient.DeleteAsync($"/api/runs/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);

        using var ownerGetResponse = await firstClient.GetAsync($"/api/runs/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, ownerGetResponse.StatusCode);
        using var ownerUpdateResponse = await firstClient.PutAsJsonAsync(
            $"/api/runs/{created.Id}",
            input with { Title = "Updated run" });
        var updated = await ownerUpdateResponse.Content.ReadFromJsonAsync<RunResponse>(JsonOptions);
        Assert.Equal(HttpStatusCode.OK, ownerUpdateResponse.StatusCode);
        Assert.Equal("Updated run", updated!.Title);
        using var ownerDeleteResponse = await firstClient.DeleteAsync($"/api/runs/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, ownerDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task Run_validation_rejects_ratings_on_planned_runs_and_race_details_on_training()
    {
        using var factory = new DiaryApiFactory();
        await factory.SeedRunTypesAsync();
        using var client = factory.CreateClient();
        var guest = await CreateGuestAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", guest.AccessToken);

        var invalidInput = new RunInput(
            DateTimeOffset.UtcNow.AddDays(1),
            RunStatus.Planned,
            "easy-run",
            "Planned run",
            Rating: new RatingInput(5),
            RaceDetails: new RaceDetailsInput(RaceName: "Not a race"));
        using var response = await client.PostAsJsonAsync("/api/runs", invalidInput);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Rating", body, StringComparison.Ordinal);
        Assert.Contains("RaceDetails", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guest_creation_is_rate_limited_per_client()
    {
        using var factory = new DiaryApiFactory();
        await factory.SeedRunTypesAsync();
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await client.PostAsync("/api/auth/guest", content: null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var limitedResponse = await client.PostAsync("/api/auth/guest", content: null);
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
    }

    private static async Task<GuestSessionResponse> CreateGuestAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/api/auth/guest", content: null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GuestSessionResponse>())!;
    }

    private sealed class DiaryApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = Guid.NewGuid().ToString("N");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:Guest:Issuer"] = "runners-diary-test-guest",
                    ["Auth:Guest:Audience"] = "runners-diary-test-api",
                    ["Auth:Guest:SigningKey"] = Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray())
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DiaryDbContext>();
                services.RemoveAll<DbContextOptions<DiaryDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<DiaryDbContext>>();
                services.AddDbContext<DiaryDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            });
        }

        public async Task SeedRunTypesAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiaryDbContext>();
            foreach (var runType in RunTypeCatalog.All)
            {
                if (!await db.RunTypes.AnyAsync(type => type.Code == runType.Code))
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

            await db.SaveChangesAsync();
        }
    }
}
