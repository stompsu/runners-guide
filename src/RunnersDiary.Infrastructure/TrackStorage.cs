using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;

namespace RunnersDiary.Infrastructure;

public interface ITrackStorage
{
    Task WriteAsync(string name, Stream content, string contentType, CancellationToken ct);
    Task<Stream> ReadAsync(string name, CancellationToken ct);
    Task DeleteAsync(string name, CancellationToken ct);
}

public sealed class TrackStorage(BlobServiceClient client, IConfiguration configuration) : ITrackStorage
{
    private BlobContainerClient Container => client.GetBlobContainerClient(
        configuration["Azure:Storage:GpxContainer"] ?? throw new InvalidOperationException("GPX storage is not configured."));

    private async Task EnsurePrivateAsync(CancellationToken ct)
    {
        var properties = await Container.GetPropertiesAsync(cancellationToken: ct);
        if (properties.Value.PublicAccess != PublicAccessType.None)
            throw new InvalidOperationException("GPX container must be private.");
    }
    public async Task WriteAsync(string name, Stream content, string contentType, CancellationToken ct)
    {
        await EnsurePrivateAsync(ct);
        await Container.GetBlobClient(name).UploadAsync(content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, ct);
    }
    public async Task<Stream> ReadAsync(string name, CancellationToken ct)
    {
        await EnsurePrivateAsync(ct);
        return await Container.GetBlobClient(name).OpenReadAsync(cancellationToken: ct);
    }
    public async Task DeleteAsync(string name, CancellationToken ct) =>
        await Container.GetBlobClient(name).DeleteIfExistsAsync(cancellationToken: ct);
}
