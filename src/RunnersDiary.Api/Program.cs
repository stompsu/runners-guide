using Azure.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<DefaultAzureCredential>();
builder.Services.AddHealthChecks()
    .AddCheck<AzureResourcesHealthCheck>("azure-resources");

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapGet("/health/live", () => TypedResults.Ok(new LivenessResponse("Healthy")))
        .WithName("GetLiveness")
        .WithTags("Infrastructure")
        .WithDescription("Local infrastructure probe only; returns no user data or diagnostic details.");
    app.MapDefaultEndpoints();
}

app.Run();

public partial class Program { }

public sealed record LivenessResponse(string Status);
