using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Azure.Identity;
using Azure;

namespace RunnersDiary.Infrastructure.Cleanup;

public sealed class ExpiredGuestCleanupWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ExpiredGuestCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var cleanup = scope.ServiceProvider.GetRequiredService<ExpiredGuestCleanupService>();
            try
            {
                await cleanup.CleanupExpiredGuestsAsync(stoppingToken);
            }
            catch (Exception exception) when (
                exception is SqlException
                    or DbUpdateException
                    or TimeoutException
                    or AuthenticationFailedException
                    or RequestFailedException)
            {
                logger.LogError(exception, "Expired guest cleanup failed; it will retry at the next interval.");
            }

            await Task.Delay(TimeSpan.FromHours(1), timeProvider, stoppingToken);
        }
    }
}
