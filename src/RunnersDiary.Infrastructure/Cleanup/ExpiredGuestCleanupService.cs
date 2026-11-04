using Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RunnersDiary.Infrastructure.Cleanup;

public sealed class ExpiredGuestCleanupService(
    DiaryDbContext db,
    IServiceProvider services,
    TimeProvider timeProvider,
    ILogger<ExpiredGuestCleanupService> logger)
{
    public async Task<int> CleanupExpiredGuestsAsync(CancellationToken cancellationToken)
    {
        await RetryPendingTrackDeletionsAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var expiredUsers = await db.Users
            .Where(user => user.IsGuest && user.ExpiresAtUtc <= now)
            .ToListAsync(cancellationToken);
        var deletedCount = 0;

        foreach (var user in expiredUsers)
        {
            var tracks = await db.GpxTracks
                .Where(track => db.Runs.Any(run => run.Id == track.RunId && run.UserId == user.Id))
                .ToListAsync(cancellationToken);

            var canDeleteUser = true;
            foreach (var track in tracks)
            {
                try
                {
                    await services.GetRequiredService<TrackDeletionService>()
                        .DeleteAsync(track.RunId, user.Id, true, cancellationToken);
                }
                catch (Exception exception) when (exception is RequestFailedException or IOException or InvalidOperationException or DbUpdateException)
                {
                    logger.LogError(exception, "Could not delete GPX blob for expired guest {GuestId}.", user.Id);
                    db.ChangeTracker.Clear();
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

    public async Task<int> RetryPendingTrackDeletionsAsync(CancellationToken cancellationToken)
    {
        // All users, not only expired guests: API deletion intent survives process restarts.
        var pending = await (
            from track in db.GpxTracks.AsNoTracking()
            join run in db.Runs.AsNoTracking() on track.RunId equals run.Id
            where track.IsPendingDeletion
            select new { track.RunId, run.UserId }).ToListAsync(cancellationToken);
        var completed = 0;
        foreach (var item in pending)
        {
            try
            {
                await services.GetRequiredService<TrackDeletionService>()
                    .DeleteAsync(item.RunId, item.UserId, false, cancellationToken, resumeOnly: true);
                completed++;
            }
            catch (Exception exception) when (exception is RequestFailedException or IOException or InvalidOperationException or DbUpdateException)
            {
                logger.LogError(exception, "Could not finish pending track deletion for run {RunId}.", item.RunId);
                // A failed final save can leave Deleted entities in this scope's tracker.
                db.ChangeTracker.Clear();
            }
        }
        return completed;
    }
}
