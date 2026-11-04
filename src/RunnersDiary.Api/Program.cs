using Azure.Identity;
using System.Net;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using RunnersDiary.Api.Authentication;
using RunnersDiary.Api.Endpoints;
using RunnersDiary.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
// Missing required Minimal API parameters must return 400, including in Development,
// rather than throwing into the generic exception handler and becoming 500.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.AddDataProtection();
builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = builder.Configuration.GetValue<int?>("Networking:ForwardedHeaders:ForwardLimit") ?? 1;
    if (options.ForwardLimit <= 0)
    {
        throw new InvalidOperationException("Networking:ForwardedHeaders:ForwardLimit must be greater than zero.");
    }

    foreach (var value in builder.Configuration.GetSection("Networking:ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    {
        if (!IPAddress.TryParse(value, out var address))
        {
            throw new InvalidOperationException($"Configured forwarded-header proxy address '{value}' is invalid.");
        }

        options.KnownProxies.Add(address);
    }

    foreach (var value in builder.Configuration.GetSection("Networking:ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        if (!System.Net.IPNetwork.TryParse(value, out var network))
        {
            throw new InvalidOperationException($"Configured forwarded-header proxy network '{value}' is invalid.");
        }

        options.KnownIPNetworks.Add(network);
    }
});
builder.Services.AddSingleton<DefaultAzureCredential>();
builder.Services.AddHttpClient("maps", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.MaxResponseContentBufferSize = 1024 * 1024;
});
var guestTokenSettings = GuestTokenSettings.Load(builder.Configuration, builder.Environment);
if (!builder.Environment.IsDevelopment()
    && (string.IsNullOrWhiteSpace(builder.Configuration["Auth:Entra:Authority"])
        || string.IsNullOrWhiteSpace(builder.Configuration["Auth:Entra:Audience"])))
{
    throw new InvalidOperationException("Auth:Entra:Authority and Auth:Entra:Audience are required outside Development.");
}

builder.Services.AddSingleton(guestTokenSettings);
builder.Services.AddSingleton<GuestTokenService>();
builder.Services.AddDiaryAuthentication(builder.Configuration, guestTokenSettings);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("maps", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("guest-bootstrap", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.AddDiaryInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck<AzureResourcesHealthCheck>("azure-resources");

var app = builder.Build();

app.UseExceptionHandler();
app.UseForwardedHeaders();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapGet("/health/live", () => TypedResults.Ok(new LivenessResponse("Healthy")))
        .AllowAnonymous()
        .WithName("GetLiveness")
        .WithTags("Infrastructure")
        .WithDescription("Local infrastructure probe only; returns no user data or diagnostic details.");
    app.MapDefaultEndpoints();
}

app.MapGuestEndpoints();
app.MapRunEndpoints();
app.MapInsightsEndpoints();
app.MapTrackEndpoints();
app.MapMapEndpoints();
app.MapFallback("{*path}", () => Results.NotFound()).AllowAnonymous();
app.Run();

public partial class Program { }

public sealed record LivenessResponse(string Status);
