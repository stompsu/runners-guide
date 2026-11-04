using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RunnersDiary.Infrastructure.Cleanup;

public sealed class ExpiredGuestCleanupService(
    DiaryDbContext db,
    IServiceProvider services,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<ExpiredGuestCleanupService> logger)
{
    public async Task<int> CleanupExpiredGuestsAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var expiredUsers = await db.Users
            .Where(user => user.IsGuest && user.ExpiresAtUtc <= now)
            .ToListAsync(cancellationToken);
        var deletedCount = 0;
        var blobClient = services.GetService<BlobServiceClient>();

        foreach (var user in expiredUsers)
        {
            var tracks = await db.GpxTracks
                .Where(track => db.Runs.Any(run => run.Id == track.RunId && run.UserId == user.Id))
                .ToListAsync(cancellationToken);

            if (tracks.Count > 0 && blobClient is null)
            {
                logger.LogError("Cannot remove expired guest {GuestId}: GPX storage is not configured.", user.Id);
                continue;
            }

            var containerName = configuration["Azure:Storage:GpxContainer"];
            if (tracks.Count > 0 && string.IsNullOrWhiteSpace(containerName))
            {
                logger.LogError("Cannot remove expired guest {GuestId}: GPX container is not configured.", user.Id);
                continue;
            }

            var canDeleteUser = true;
            foreach (var track in tracks)
            {
                try
                {
                    await blobClient!.GetBlobContainerClient(
                            containerName!)
                        .GetBlobClient(track.BlobName)
                        .DeleteIfExistsAsync(cancellationToken: cancellationToken);
                }
                catch (RequestFailedException exception) when (exception.Status != 404)
                {
                    logger.LogError(exception, "Could not delete GPX blob for expired guest {GuestId}.", user.Id);
                    canDeleteUser = false;
                    break;
                }
            }

            if (!canDeleteUser)
            {
                continue;
            }

            db.Users.Remove(user);
            await db.SaveChangesAsync(cancellationToken);
            deletedCount++;
        }

        return deletedCount;
    }
}
