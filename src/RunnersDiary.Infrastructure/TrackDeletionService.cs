using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace RunnersDiary.Infrastructure;

/// <summary>Durable, idempotent deletion of a track and optionally its parent run.</summary>
public sealed class TrackDeletionService(DiaryDbContext db, IServiceProvider services)
{
    public async Task<bool> DeleteAsync(Guid runId, string userId, bool deleteRun, CancellationToken ct,
        bool resumeOnly = false)
    {
        // Commit intent independently: no storage operation may precede this commit.
        await using (var transaction = await LockUserAsync(userId, ct))
        {
            var run = await db.Runs.SingleOrDefaultAsync(r => r.Id == runId && r.UserId == userId, ct);
            if (run is null) return false;
            var track = await db.GpxTracks.SingleOrDefaultAsync(t => t.RunId == runId, ct);
            if (track is not null)
            {
                await db.Entry(track).ReloadAsync(ct);
                // A worker's candidate snapshot can be stale: an API retry may have
                // finished, followed by a new upload. Never mark that new track.
                if (resumeOnly && !track.IsPendingDeletion) return true;
                track.IsPendingDeletion = true;
                track.DeleteParentRun |= deleteRun;
                await db.SaveChangesAsync(ct);
            }
            else if (deleteRun && !resumeOnly)
            {
                db.Runs.Remove(run);
                await db.SaveChangesAsync(ct);
            }
            if (transaction is not null) await transaction.CommitAsync(ct);
        }

        // Re-read under the same lock used by uploads and other deletion attempts.
        // Holding it through finalization prevents a retry deleting a replacement.
        await using (var transaction = await LockUserAsync(userId, ct))
        {
            var track = await db.GpxTracks.SingleOrDefaultAsync(t => t.RunId == runId, ct);
            if (track is null)
            {
                if (transaction is not null) await transaction.CommitAsync(ct);
                return true;
            }
            await db.Entry(track).ReloadAsync(ct);
            if (track.IsPendingDeletion)
            {
                var storage = services.GetRequiredService<ITrackStorage>();
                await storage.DeleteAsync(track.BlobName, ct);
                if (track.ProcessedBlobName is not null)
                    await storage.DeleteAsync(track.ProcessedBlobName, ct);
                if (track.DeleteParentRun)
                {
                    var run = await db.Runs.SingleAsync(r => r.Id == runId && r.UserId == userId, ct);
                    db.Runs.Remove(run);
                }
                db.GpxTracks.Remove(track);
                await db.SaveChangesAsync(ct);
            }
            if (transaction is not null) await transaction.CommitAsync(ct);
        }
        return true;
    }

    private async Task<IDbContextTransaction?> LockUserAsync(string userId, CancellationToken ct)
    {
        var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            if (db.Database.IsSqlServer())
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT Id FROM Users WITH (UPDLOCK, HOLDLOCK) WHERE Id = {userId}", ct);
            return transaction;
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            throw;
        }
    }
}
