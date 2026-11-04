using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace RunnersDiary.Api.Authentication;

public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "A verified Entra External ID or app-issued guest access token."
        };

        foreach (var (path, pathItem) in document.Paths)
        {
            if (!path.StartsWith("/api/runs", StringComparison.Ordinal)
                && !path.StartsWith("/api/maps", StringComparison.Ordinal))
            {
                continue;
            }

            if (pathItem?.Operations is not { } operations)
            {
                continue;
            }

            foreach (var operation in operations.Values)
            {
                var security = operation.Security ?? [];
                security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                });
                operation.Security = security;
            }
        }

        return Task.CompletedTask;
    }
}
