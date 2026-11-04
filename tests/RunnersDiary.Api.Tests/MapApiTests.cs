using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RunnersDiary.Api.Endpoints;

public sealed class MapApiTests
{
    [Fact]
    public async Task Maps_issues_short_lived_token_and_bounds_provider_neutral_geocoding()
    {
        var identity = new MapCredential();
        using var root = new RunApiTests.DiaryApiFactory();
        using var factory = root.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Azure:Maps:ClientId"] = "11111111-1111-1111-1111-111111111111" }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DefaultAzureCredential>();
                services.AddSingleton<DefaultAzureCredential>(identity);
                services.AddHttpClient("maps").ConfigurePrimaryHttpMessageHandler(() => new SearchHandler());
            });
        });
        using var client = factory.CreateClient();
        var guest = (await (await client.PostAsync("/api/auth/guest", null))
            .Content.ReadFromJsonAsync<GuestSessionResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", guest.AccessToken);
        using var token = await client.GetAsync("/api/maps/token");
        Assert.True(token.Headers.CacheControl!.NoStore);
        var issued = (await token.Content.ReadFromJsonAsync<MapTokenResponse>())!;
        Assert.Equal("synthetic-oauth-token", issued.AccessToken);
        Assert.Equal("11111111-1111-1111-1111-111111111111", issued.ClientId);
        Assert.InRange(issued.ExpiresAtUtc, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(6));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/maps/geocode?query=")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/maps/geocode?query=" + new string('a', 201))).StatusCode);
        var results = await client.GetFromJsonAsync<LocationResult[]>("/api/maps/geocode?query=Gliwice");
        Assert.Equal(5, results!.Length);
        Assert.Equal(new LocationResult("Synthetic location", 50, 18), results[0]);
        Assert.All(identity.Scopes, scope => Assert.Equal("https://atlas.microsoft.com/.default", scope));
    }

    private sealed class MapCredential : DefaultAzureCredential
    {
        public List<string> Scopes { get; } = [];
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken = default)
        {
            Scopes.AddRange(requestContext.Scopes);
            return ValueTask.FromResult(new AccessToken("synthetic-oauth-token", DateTimeOffset.UtcNow.AddMinutes(5)));
        }
    }

    private sealed class SearchHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("atlas.microsoft.com", request.RequestUri!.Host);
            Assert.Contains("limit=5", request.RequestUri.Query, StringComparison.Ordinal);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("synthetic-oauth-token", request.Headers.Authorization.Parameter);
            Assert.True(request.Headers.Contains("x-ms-client-id"));
            const string item = """{"address":{"freeformAddress":"Synthetic location"},"position":{"lat":50,"lon":18}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"results\":[" + string.Join(',', Enumerable.Repeat(item, 7)) + "]}", Encoding.UTF8, "application/json")
            });
        }
    }
}
