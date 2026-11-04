using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Liveness_returns_a_fixed_success_response()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"status":"Healthy"}""", body);
    }

    [Fact]
    public async Task OpenApi_document_includes_the_liveness_probe()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        var document = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/health/live", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readiness_fails_when_external_azure_resources_are_not_configured()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Azure:Sql:Server"] = null,
                    ["Azure:Sql:Database"] = null,
                    ["Azure:Storage:BlobServiceUri"] = null,
                    ["Azure:Storage:GpxContainer"] = null
                })));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Infrastructure_routes_are_not_mapped_outside_development()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production));
        using var client = factory.CreateClient();

        using var livenessResponse = await client.GetAsync("/health/live");
        using var readinessResponse = await client.GetAsync("/health/ready");
        using var openApiResponse = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.NotFound, livenessResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, readinessResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, openApiResponse.StatusCode);
    }
}
