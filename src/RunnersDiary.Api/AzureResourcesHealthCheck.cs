using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

public sealed class AzureResourcesHealthCheck(
    IConfiguration configuration,
    DefaultAzureCredential credential,
    ILogger<AzureResourcesHealthCheck> logger) : IHealthCheck
{
    private const string SqlScope = "https://database.windows.net/.default";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var sqlServer = configuration["Azure:Sql:Server"];
        var sqlDatabase = configuration["Azure:Sql:Database"];
        var blobServiceUri = configuration["Azure:Storage:BlobServiceUri"];
        var blobContainer = configuration["Azure:Storage:GpxContainer"];

        if (string.IsNullOrWhiteSpace(sqlServer)
            || string.IsNullOrWhiteSpace(sqlDatabase)
            || !Uri.TryCreate(blobServiceUri, UriKind.Absolute, out var serviceUri)
            || serviceUri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(blobContainer))
        {
            return HealthCheckResult.Unhealthy("Azure development resources are not configured.");
        }

        try
        {
            var token = await credential.GetTokenAsync(
                new TokenRequestContext([SqlScope]),
                cancellationToken);
            var connectionString = new SqlConnectionStringBuilder
            {
                DataSource = sqlServer,
                InitialCatalog = sqlDatabase,
                Encrypt = true,
                TrustServerCertificate = false,
                ConnectTimeout = 5,
                Pooling = false
            }.ConnectionString;

            await using (var connection = new SqlConnection(connectionString)
            {
                AccessToken = token.Token
            })
            {
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                await command.ExecuteScalarAsync(cancellationToken);
            }

            var blobClient = new BlobServiceClient(serviceUri, credential);
            var containerClient = blobClient.GetBlobContainerClient(blobContainer);
            await containerClient.GetPropertiesAsync(cancellationToken: cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (
            exception is AuthenticationFailedException
                or SqlException
                or RequestFailedException)
        {
            logger.LogError(exception, "Azure development resource readiness check failed.");
            return HealthCheckResult.Unhealthy("Azure development resources are unavailable.");
        }
    }
}
