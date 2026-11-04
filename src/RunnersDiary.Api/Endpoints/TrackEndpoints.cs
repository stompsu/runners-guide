using System.Data;
using System.Security.Claims;
using System.Text.Json;
using System.Xml;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Domain;
using RunnersDiary.Infrastructure;

namespace RunnersDiary.Api.Endpoints;

public static class TrackEndpoints
{
    public static void MapTrackEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/runs/{id:guid}/track").RequireAuthorization().WithTags("Tracks");
        group.MapPost("", Upload).Accepts<Stream>("application/gpx+xml")
            .Produces<ProcessedTrack>().Produces(400).Produces(404).Produces(413).Produces(409);
        group.MapGet("", Get).Produces<ProcessedTrack>().Produces(404);
        group.MapGet("/original", Original).Produces<Stream>(200, contentType: "application/gpx+xml").Produces(404);
        group.MapDelete("", Delete).Produces(204).Produces(404);
    }

    private static async Task<IResult> Upload(Guid id, bool planned, HttpContext context,
        DiaryDbContext db, TimeProvider time, ITrackStorage storage, CancellationToken ct)
    {
        var user = await RunEndpoints.GetCurrentUserAsync(context.User, db, time, ct);
        if (user is null) return Results.Unauthorized();
        if (!await db.Runs.AnyAsync(r => r.Id == id && r.UserId == user.Id, ct)) return Results.NotFound();
        var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = GpxParser.MaxBytes;
        if (context.Request.ContentLength > GpxParser.MaxBytes) return Results.StatusCode(413);
        if (context.Request.ContentType?.Split(';')[0].Trim().ToLowerInvariant() is not ("application/gpx+xml" or "application/xml" or "text/xml"))
            return Results.BadRequest(new { message = "Use application/gpx+xml." });
        using var raw = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        try
        {
            while ((read = await context.Request.Body.ReadAsync(buffer, ct)) > 0)
            {
                if (raw.Length + read > GpxParser.MaxBytes) return Results.StatusCode(413);
                await raw.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        catch (BadHttpRequestException e) when (e.StatusCode == 413) { return Results.StatusCode(413); }
        raw.Position = 0;
        ProcessedTrack processed;
        try { processed = GpxParser.Parse(raw, planned); }
        catch (Exception e) when (e is XmlException or FormatException or InvalidOperationException)
        { return Results.BadRequest(new { message = "Invalid GPX track." }); }

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        // All uploads/deletions for this user acquire the same SQL lock, across API replicas.
        if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT Id FROM Users WITH (UPDLOCK, HOLDLOCK) WHERE Id = {user.Id}", ct);
        if (!await db.Runs.AnyAsync(r => r.Id == id && r.UserId == user.Id, ct)) return Results.NotFound();
        var old = await db.GpxTracks.SingleOrDefaultAsync(t => t.RunId == id, ct);
        if (old is { IsPendingDeletion: true })
            return Results.Conflict(new { message = "Track deletion is pending." });
        var oldRaw = old?.BlobName;
        var oldProcessed = old?.ProcessedBlobName;
        // The user was initially loaded before acquiring the SQL lock.
        await db.Entry(user).ReloadAsync(ct);
        if (user.IsGuest && user.GpxUploadRequestCount >= 10)
            return Results.Conflict(new { message = "Guest accounts may upload GPX at most 10 times." });
        var name = Guid.NewGuid().ToString("N");
        var rawName = $"{name}/original.gpx"; var processedName = $"{name}/track.json";
        try
        {
            raw.Position = 0;
            await storage.WriteAsync(rawName, raw, "application/gpx+xml", ct);
            using var json = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(processed, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await storage.WriteAsync(processedName, json, "application/geo+json", ct);
            var track = old ?? new GpxTrack { RunId = id, BlobName = rawName };
            if (old is null) db.GpxTracks.Add(track);
            track.BlobName = rawName; track.ProcessedBlobName = processedName;
            track.UploadedAtUtc = time.GetUtcNow(); track.FileSizeBytes = raw.Length; track.IsPlannedCourse = planned;
            if (user.IsGuest) user.GpxUploadRequestCount++;
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
        }
        catch
        {
            await BestEffortDelete(storage, rawName);
            await BestEffortDelete(storage, processedName);
            throw;
        }
        if (oldRaw is not null) await BestEffortDelete(storage, oldRaw);
        if (oldProcessed is not null) await BestEffortDelete(storage, oldProcessed);
        return Results.Ok(processed);
    }

    private static async Task<IResult> Get(Guid id, ClaimsPrincipal principal, DiaryDbContext db,
        TimeProvider time, ITrackStorage storage, HttpContext context, CancellationToken ct)
    {
        var user = await RunEndpoints.GetCurrentUserAsync(principal, db, time, ct);
        if (user is null) return Results.Unauthorized();
        var track = await db.GpxTracks.AsNoTracking().SingleOrDefaultAsync(t => t.RunId == id && !t.IsPendingDeletion &&
            db.Runs.Any(r => r.Id == id && r.UserId == user.Id), ct);
        if (track?.ProcessedBlobName is null) return Results.NotFound();
        context.Response.Headers.CacheControl = "no-store";
        await using var stream = await storage.ReadAsync(track.ProcessedBlobName, ct);
        return Results.Ok(await JsonSerializer.DeserializeAsync<ProcessedTrack>(stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web), ct));
    }

    private static async Task<IResult> Delete(Guid id, ClaimsPrincipal principal, DiaryDbContext db,
        TimeProvider time, TrackDeletionService deletion, CancellationToken ct)
    {
        var user = await RunEndpoints.GetCurrentUserAsync(principal, db, time, ct);
        if (user is null) return Results.Unauthorized();
        return await deletion.DeleteAsync(id, user.Id, false, ct) ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> Original(Guid id, ClaimsPrincipal principal, DiaryDbContext db,
        TimeProvider time, ITrackStorage storage, HttpContext context, CancellationToken ct)
    {
        var user = await RunEndpoints.GetCurrentUserAsync(principal, db, time, ct);
        if (user is null) return Results.Unauthorized();
        var track = await db.GpxTracks.AsNoTracking().SingleOrDefaultAsync(t => t.RunId == id && !t.IsPendingDeletion &&
            db.Runs.Any(r => r.Id == id && r.UserId == user.Id), ct);
        if (track is null) return Results.NotFound();
        context.Response.Headers.CacheControl = "no-store";
        return Results.Stream(await storage.ReadAsync(track.BlobName, ct),
            "application/gpx+xml", "track.gpx");
    }

    private static async Task BestEffortDelete(ITrackStorage storage, string name)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await storage.DeleteAsync(name, timeout.Token); }
        catch { /* Do not hide the original storage/DB failure. */ }
    }
}
