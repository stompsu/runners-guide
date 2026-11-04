using Azure.Core;
using Azure.Identity;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;

namespace RunnersDiary.Api.Endpoints;

public sealed record MapTokenResponse(string ClientId, string AccessToken, DateTimeOffset ExpiresAtUtc);
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record LocationResult(string DisplayName, double Latitude, double Longitude);

public static class MapEndpoints
{
    public static void MapMapEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/maps").RequireAuthorization().RequireRateLimiting("maps").WithTags("Maps");
        group.MapGet("/token", async (DefaultAzureCredential credential, IConfiguration config,
            HttpContext context, CancellationToken ct) =>
        {
            var clientId = config["Azure:Maps:ClientId"];
            if (!Guid.TryParse(clientId, out _)) return Results.Problem("Azure Maps client ID is not configured.", statusCode: 503);
            var token = await credential.GetTokenAsync(new TokenRequestContext(["https://atlas.microsoft.com/.default"]), ct);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new MapTokenResponse(clientId, token.Token, token.ExpiresOn));
        }).Produces<MapTokenResponse>();
        group.MapGet("/geocode", async (string query, DefaultAzureCredential credential, IConfiguration config,
            IHttpClientFactory clients, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (string.IsNullOrWhiteSpace(query) || query.Length > 200)
                return Results.BadRequest();
            var clientId = config["Azure:Maps:ClientId"];
            if (!Guid.TryParse(clientId, out _)) return Results.Problem(statusCode: 503);
            var token = await credential.GetTokenAsync(new TokenRequestContext(["https://atlas.microsoft.com/.default"]), ct);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://atlas.microsoft.com/search/address/json?api-version=1.0&limit=5&query={Uri.EscapeDataString(query.Trim())}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            request.Headers.Add("x-ms-client-id", clientId);
            using var response = await clients.CreateClient("maps").SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return Results.Problem("Geocoding is unavailable.", statusCode: 502);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            var results = json.RootElement.GetProperty("results").EnumerateArray().Take(5).Select(item =>
                new LocationResult(item.GetProperty("address").GetProperty("freeformAddress").GetString() ?? "Location",
                    item.GetProperty("position").GetProperty("lat").GetDouble(),
                    item.GetProperty("position").GetProperty("lon").GetDouble())).ToArray();
            return Results.Ok(results);
        }).Produces<LocationResult[]>();
    }
}
