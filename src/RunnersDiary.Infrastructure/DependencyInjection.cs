using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RunnersDiary.Infrastructure.Cleanup;

namespace RunnersDiary.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDiaryInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var sqlServer = configuration["Azure:Sql:Server"];
        var sqlDatabase = configuration["Azure:Sql:Database"];
        var hasSqlConfiguration = !string.IsNullOrWhiteSpace(sqlServer)
            && !string.IsNullOrWhiteSpace(sqlDatabase);
        var sqlConnection = new SqlConnectionStringBuilder
        {
            DataSource = hasSqlConfiguration ? $"tcp:{sqlServer},1433" : "not-configured",
            InitialCatalog = hasSqlConfiguration ? sqlDatabase : "not-configured",
            Encrypt = true,
            TrustServerCertificate = false,
            Authentication = SqlAuthenticationMethod.ActiveDirectoryDefault,
            ConnectTimeout = 5
        }.ConnectionString;

        services.AddDbContext<DiaryDbContext>(options =>
            options.UseSqlServer(sqlConnection, sql => sql.UseNetTopologySuite()));
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<DemoDataSeeder>();
        services.AddScoped<ITrackStorage>(provider => new TrackStorage(
            provider.GetRequiredService<BlobServiceClient>(), configuration));
        services.AddScoped<ExpiredGuestCleanupService>();
        services.AddScoped<TrackDeletionService>();

        var blobServiceUri = configuration["Azure:Storage:BlobServiceUri"];
        if (Uri.TryCreate(blobServiceUri, UriKind.Absolute, out var serviceUri)
            && serviceUri.Scheme == Uri.UriSchemeHttps)
        {
            services.AddSingleton(serviceProvider =>
                new BlobServiceClient(
                    serviceUri,
                    serviceProvider.GetRequiredService<DefaultAzureCredential>()));
        }

        if (hasSqlConfiguration)
        {
            services.AddHostedService<ExpiredGuestCleanupWorker>();
        }

        return services;
    }
}
