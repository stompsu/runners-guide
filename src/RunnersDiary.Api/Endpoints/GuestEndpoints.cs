using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Api.Authentication;
using RunnersDiary.Infrastructure;

namespace RunnersDiary.Api.Endpoints;

public static class GuestEndpoints
{
    public static void MapGuestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/guest", CreateGuestAsync)
            .AllowAnonymous()
            .RequireRateLimiting("guest-bootstrap")
            .WithTags("Authentication")
            .WithSummary("Create an isolated 24-hour demo account and issue a guest token.")
            .Produces<GuestSessionResponse>()
            .Produces(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<IResult> CreateGuestAsync(
        DemoDataSeeder seeder,
        GuestTokenService tokenService,
        CancellationToken cancellationToken)
    {
        var guest = await seeder.CreateGuestAsync(cancellationToken);
        var token = tokenService.Create(guest);
        return Results.Ok(new GuestSessionResponse(token.AccessToken, token.ExpiresAtUtc));
    }
}

public sealed record GuestSessionResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);
