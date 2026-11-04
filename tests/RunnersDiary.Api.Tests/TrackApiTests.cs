using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Api.Endpoints;
using RunnersDiary.Domain;
using RunnersDiary.Infrastructure;
using RunnersDiary.Infrastructure.Cleanup;

public sealed class MemoryTrackStorage : ITrackStorage
{
    public Dictionary<string, byte[]> Files { get; } = [];
    public bool FailProcessedWrite { get; set; }
    public bool FailDelete { get; set; }
    public bool FailProcessedDelete { get; set; }
    public Task DeleteAsync(string name, CancellationToken ct)
    {
        if (FailDelete) throw new IOException("Synthetic storage failure.");
        if (FailProcessedDelete && name.EndsWith(".json", StringComparison.Ordinal)) throw new IOException("Synthetic partial delete failure.");
        Files.Remove(name);
        return Task.CompletedTask;
    }
    public Task<Stream> ReadAsync(string name, CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream(Files[name]));
    public async Task WriteAsync(string name, Stream content, string contentType, CancellationToken ct)
    {
        if (FailProcessedWrite && name.EndsWith(".json", StringComparison.Ordinal)) throw new IOException("Synthetic write failure.");
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        Files.Add(name, buffer.ToArray());
    }
}

public sealed class FailingSaveInterceptor : SaveChangesInterceptor
{
    public bool Fail { get; set; }
    public int? FailOnSave { get; set; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (FailOnSave is not null) FailOnSave--;
        return Fail || FailOnSave == 0
            ? throw new DbUpdateException("Synthetic DB failure.") : ValueTask.FromResult(result);
    }
}

public sealed class TrackApiTests
{
    internal const string Gpx = """
        <gpx xmlns="http://www.topografix.com/GPX/1/1" version="1.1"><trk><trkseg>
        <trkpt lat="50" lon="18"><ele>100</ele><time>2026-01-01T10:00:00Z</time></trkpt>
        <trkpt lat="50.001" lon="18.002"><ele>105</ele><time>2026-01-01T10:01:00Z</time></trkpt>
        </trkseg></trk></gpx>
        """;
    private static Task<HttpResponseMessage> Upload(HttpClient client, Guid id, string gpx = Gpx, bool planned = false) =>
        client.PostAsync($"/api/runs/{id}/track?planned={planned}", new StringContent(gpx, Encoding.UTF8, "application/gpx+xml"));

    private static async Task<Guid[]> Guest(RunApiTests.DiaryApiFactory factory, HttpClient client)
    {
        var result = await client.PostAsync("/api/auth/guest", null);
        result.EnsureSuccessStatusCode();
        var session = (await result.Content.ReadFromJsonAsync<GuestSessionResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiaryDbContext>();
        var user = await db.Users.OrderByDescending(u => u.CreatedAtUtc).FirstAsync();
        return await db.Runs.Where(r => r.UserId == user.Id).Select(r => r.Id).ToArrayAsync();
    }

    [Fact]
    public async Task Auth_owner_isolation_preview_replacement_and_delete()
    {
        using var factory = new RunApiTests.DiaryApiFactory();
        using var first = factory.CreateClient();
        using var other = factory.CreateClient();
        using var anonymous = factory.CreateClient();
        var ids = await Guest(factory, first);
        await Guest(factory, other);
        var id = ids[0];
        foreach (var route in new[] { $"/api/runs/{id}/track", "/api/maps/token", "/api/maps/geocode?query=Gliwice" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(anonymous, id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Upload(other, id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/runs/{id}/track")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/runs/{id}/track/original")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/runs/{id}/track")).StatusCode);
        var before = await first.GetStringAsync($"/api/runs/{id}");
        var uploaded = await Upload(first, id);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var preview = await uploaded.Content.ReadFromJsonAsync<ProcessedTrack>();
        Assert.Equal(60, preview!.Features[0].Properties.DurationSeconds);
        Assert.False(preview.IsPlannedCourse);
        Assert.Equal(before, await first.GetStringAsync($"/api/runs/{id}"));
        Assert.Equal(2, factory.Storage.Files.Count);
        var original = await first.GetAsync($"/api/runs/{id}/track/original");
        Assert.Equal(Gpx, await original.Content.ReadAsStringAsync());
        Assert.True(original.Headers.CacheControl!.NoStore);
        Assert.Equal(HttpStatusCode.OK, (await Upload(first, id, planned: true)).StatusCode);
        Assert.Equal(2, factory.Storage.Files.Count);
        var get = await first.GetAsync($"/api/runs/{id}/track");
        Assert.True((await get.Content.ReadFromJsonAsync<ProcessedTrack>())!.IsPlannedCourse);
        Assert.True(get.Headers.CacheControl!.NoStore);
        Assert.Equal(HttpStatusCode.NoContent, (await first.DeleteAsync($"/api/runs/{id}/track")).StatusCode);
        Assert.Empty(factory.Storage.Files);
        Assert.Equal(HttpStatusCode.NotFound, (await first.GetAsync($"/api/runs/{id}/track")).StatusCode);
    }

    [Fact]
    public async Task Storage_and_db_failure_preserve_previous_metadata_and_clean_new_artifacts()
    {
        using var factory = new RunApiTests.DiaryApiFactory();
        using var client = factory.CreateClient();
        var id = (await Guest(factory, client))[0];
        (await Upload(client, id)).EnsureSuccessStatusCode();
        var oldNames = factory.Storage.Files.Keys.Order().ToArray();
        factory.Storage.FailProcessedWrite = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await Upload(client, id)).StatusCode);
        Assert.Equal(oldNames, factory.Storage.Files.Keys.Order().ToArray());
        factory.Storage.FailProcessedWrite = false;
        factory.Saves.Fail = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await Upload(client, id)).StatusCode);
        factory.Saves.Fail = false;
        Assert.Equal(oldNames, factory.Storage.Files.Keys.Order().ToArray());
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(1, (await scope.ServiceProvider.GetRequiredService<DiaryDbContext>().Users.SingleAsync()).GpxUploadRequestCount);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/runs/{id}/track")).StatusCode);
        factory.Storage.FailDelete = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.DeleteAsync($"/api/runs/{id}/track")).StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.DeleteAsync($"/api/runs/{id}")).StatusCode);
        using (var scope = factory.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<DiaryDbContext>().GpxTracks.AnyAsync(t => t.RunId == id));
        factory.Storage.FailDelete = false;
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/runs/{id}")).StatusCode);
        Assert.Empty(factory.Storage.Files);
    }

    [Fact]
    public async Task Guest_lifetime_limit_counts_replacements_and_deletion_does_not_refund()
    {
        using var factory = new RunApiTests.DiaryApiFactory();
        using var client = factory.CreateClient();
        var ids = await Guest(factory, client);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, ids[0], "<gpx>")).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,
            (await Upload(client, ids[0], new string('x', GpxParser.MaxBytes + 1))).StatusCode);
        // No Content-Length: exercise the stream-boundary check as well.
        using var stream = new NonSeekableStream(new byte[GpxParser.MaxBytes + 1]);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,
            (await client.PostAsync($"/api/runs/{ids[0]}/track?planned=false", content)).StatusCode);
        Assert.Empty(factory.Storage.Files);
        // Ten requests against the same row must exhaust the lifetime allowance.
        for (var i = 0; i < 10; i++) Assert.Equal(HttpStatusCode.OK, (await Upload(client, ids[0])).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Upload(client, ids[10])).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Upload(client, ids[0])).StatusCode);
        Assert.Equal(2, factory.Storage.Files.Count);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/runs/{ids[0]}/track")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Upload(client, ids[10])).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(10, (await scope.ServiceProvider.GetRequiredService<DiaryDbContext>().Users.SingleAsync()).GpxUploadRequestCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Final_delete_save_failure_leaves_durable_intent_and_retry_finishes(bool deleteRun, bool workerRetry)
    {
        using var factory = new RunApiTests.DiaryApiFactory();
        using var client = factory.CreateClient();
        var id = (await Guest(factory, client))[0];
        (await Upload(client, id)).EnsureSuccessStatusCode();
        var route = $"/api/runs/{id}" + (deleteRun ? "" : "/track");
        factory.Saves.FailOnSave = 2; // Intent saves, storage succeeds, final metadata save fails.
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.DeleteAsync(route)).StatusCode);
        factory.Saves.FailOnSave = null;
        Assert.Empty(factory.Storage.Files);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiaryDbContext>();
            var pending = await db.GpxTracks.SingleAsync(t => t.RunId == id);
            Assert.True(pending.IsPendingDeletion);
            Assert.Equal(deleteRun, pending.DeleteParentRun);
            Assert.True(await db.Runs.AnyAsync(r => r.Id == id));
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/runs/{id}/track")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/runs/{id}/track/original")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Upload(client, id)).StatusCode);
        if (workerRetry)
        {
            using var scope = factory.Services.CreateScope();
            // Same entry point invoked periodically by ExpiredGuestCleanupWorker.
            await scope.ServiceProvider.GetRequiredService<ExpiredGuestCleanupService>()
                .CleanupExpiredGuestsAsync(CancellationToken.None);
        }
        else
        {
            // Track DELETE must preserve the durable parent-run flag as well.
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/runs/{id}/track")).StatusCode);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiaryDbContext>();
            Assert.False(await db.GpxTracks.AnyAsync(t => t.RunId == id));
            Assert.Equal(!deleteRun, await db.Runs.AnyAsync(r => r.Id == id));
        }
        Assert.Empty(factory.Storage.Files);
        Assert.Equal(deleteRun ? HttpStatusCode.NotFound : HttpStatusCode.NoContent,
            (await client.DeleteAsync(route)).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Intent_save_failure_does_not_delete_any_blob(bool deleteRun)
    {
        using var factory = new RunApiTests.DiaryApiFactory();
        using var client = factory.CreateClient();
        var id = (await Guest(factory, client))[0];
        (await Upload(client, id)).EnsureSuccessStatusCode();
        factory.Saves.FailOnSave = 1;
        Assert.Equal(HttpStatusCode.InternalServerError,
            (await client.DeleteAsync($"/api/runs/{id}" + (deleteRun ? "" : "/track"))).StatusCode);
        factory.Saves.FailOnSave = null;
        Assert.Equal(2, factory.Storage.Files.Count);
        using var scope = factory.Services.CreateScope();
        var track = await scope.ServiceProvider.GetRequiredService<DiaryDbContext>().GpxTracks.SingleAsync(t => t.RunId == id);
        Assert.False(track.IsPendingDeletion);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/runs/{id}/track")).StatusCode);
    }

    [Fact]
    public async Task Worker_final_save_failure_can_retry_in_the_same_scope_and_stale_candidates_skip_new_uploads()
    {
        using var factory = new RunApiTests.DiaryApiFactory();
        using var client = factory.CreateClient();
        var id = (await Guest(factory, client))[0];
        (await Upload(client, id)).EnsureSuccessStatusCode();
        factory.Storage.FailDelete = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.DeleteAsync($"/api/runs/{id}/track")).StatusCode);
        factory.Storage.FailDelete = false;
        using var scope = factory.Services.CreateScope();
        var cleanup = scope.ServiceProvider.GetRequiredService<ExpiredGuestCleanupService>();
        factory.Saves.FailOnSave = 2;
        Assert.Equal(0, await cleanup.RetryPendingTrackDeletionsAsync(CancellationToken.None));
        factory.Saves.FailOnSave = null;
        var db = scope.ServiceProvider.GetRequiredService<DiaryDbContext>();
        Assert.True((await db.GpxTracks.AsNoTracking().SingleAsync(t => t.RunId == id)).IsPendingDeletion);
        Assert.Empty(factory.Storage.Files);
        Assert.Equal(1, await cleanup.RetryPendingTrackDeletionsAsync(CancellationToken.None));
        Assert.Equal(0, await cleanup.RetryPendingTrackDeletionsAsync(CancellationToken.None));
        (await Upload(client, id)).EnsureSuccessStatusCode();
        // Simulate a candidate collected before the previous pending deletion completed.
        var user = await db.Users.SingleAsync();
        await scope.ServiceProvider.GetRequiredService<TrackDeletionService>()
            .DeleteAsync(id, user.Id, false, CancellationToken.None, resumeOnly: true);
        Assert.Equal(2, factory.Storage.Files.Count);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/runs/{id}/track")).StatusCode);
    }

    [Fact]
    public async Task Partial_delete_and_expired_guest_cleanup_keep_metadata_for_retry()
    {
        using var factory = new RunApiTests.DiaryApiFactory();
        using var client = factory.CreateClient();
        var id = (await Guest(factory, client))[0];
        (await Upload(client, id)).EnsureSuccessStatusCode();
        factory.Storage.FailProcessedDelete = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.DeleteAsync($"/api/runs/{id}/track")).StatusCode);
        Assert.Single(factory.Storage.Files);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiaryDbContext>();
            Assert.True(await db.GpxTracks.AnyAsync(t => t.RunId == id && t.IsPendingDeletion));
            var user = await db.Users.SingleAsync();
            user.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
            var cleanup = scope.ServiceProvider.GetRequiredService<ExpiredGuestCleanupService>();
            Assert.Equal(0, await cleanup.CleanupExpiredGuestsAsync(CancellationToken.None));
            Assert.True(await db.Users.AnyAsync());
            factory.Storage.FailProcessedDelete = false;
            Assert.Equal(1, await cleanup.CleanupExpiredGuestsAsync(CancellationToken.None));
            Assert.False(await db.Users.AnyAsync());
            Assert.Empty(factory.Storage.Files);
        }
    }

    [Fact]
    public async Task Exact_twenty_MiB_file_is_accepted()
    {
        using var factory = new RunApiTests.DiaryApiFactory();
        using var client = factory.CreateClient();
        var id = (await Guest(factory, client))[0];
        var exact = Gpx + new string(' ', GpxParser.MaxBytes - Encoding.UTF8.GetByteCount(Gpx));
        Assert.Equal(HttpStatusCode.OK, (await Upload(client, id, exact)).StatusCode);
        Assert.Contains(factory.Storage.Files.Values, bytes => bytes.Length == GpxParser.MaxBytes);
    }

    [Fact]
    public async Task Configured_demo_guests_get_isolated_synthetic_planned_and_recorded_tracks()
    {
        using var root = new RunApiTests.DiaryApiFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            // Presence enables seeding; all I/O still goes to MemoryTrackStorage, never this client.
            services.AddSingleton(new BlobServiceClient(new Uri("https://synthetic-storage.example.test")))));
        using var client = factory.CreateClient();
        (await client.PostAsync("/api/auth/guest", null)).EnsureSuccessStatusCode();
        (await client.PostAsync("/api/auth/guest", null)).EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiaryDbContext>();
        Assert.Equal(2, await db.Users.CountAsync());
        Assert.Equal(12, await db.GpxTracks.CountAsync());
        Assert.Equal(4, await db.GpxTracks.CountAsync(t => t.IsPlannedCourse));
        Assert.Equal(24, root.Storage.Files.Count);
        Assert.Equal(12, await db.GpxTracks.Select(t => t.BlobName).Distinct().CountAsync());
        Assert.All(await db.GpxTracks.ToArrayAsync(), t => Assert.NotNull(t.ProcessedBlobName));
        Assert.All(await db.Users.ToArrayAsync(), u => Assert.Equal(0, u.GpxUploadRequestCount));
    }

    [Fact]
    public async Task Seeded_tracks_do_not_consume_the_ten_upload_request_allowance()
    {
        using var root = new RunApiTests.DiaryApiFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton(new BlobServiceClient(new Uri("https://synthetic-storage.example.test")))));
        using var client = factory.CreateClient();
        var response = await client.PostAsync("/api/auth/guest", null);
        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<GuestSessionResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        Guid[] ids;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiaryDbContext>();
            ids = await db.Runs.Where(r => !db.GpxTracks.Any(t => t.RunId == r.Id))
                .Take(10).Select(r => r.Id).ToArrayAsync();
        }
        foreach (var id in ids) Assert.Equal(HttpStatusCode.OK, (await Upload(client, id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Upload(client, ids[0])).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiaryDbContext>();
            Assert.Equal(16, await db.GpxTracks.CountAsync());
            Assert.Equal(10, (await db.Users.SingleAsync()).GpxUploadRequestCount);
        }
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
