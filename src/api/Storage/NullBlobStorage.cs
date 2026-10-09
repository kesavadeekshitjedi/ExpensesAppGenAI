namespace Expenses.Api.Storage;

// Registered when Storage:BlobEndpoint is not configured (local dev without Azure). Any attempt to
// store or read a blob fails clearly rather than the app failing to start, so the rest of the API
// still runs. Tests replace this with an in-memory fake.
public sealed class NullBlobStorage : IBlobStorage
{
    private static InvalidOperationException NotConfigured() =>
        new("Blob storage is not configured (set Storage:BlobEndpoint). Pictures and receipt images are unavailable.");

    public Task UploadAsync(string container, string blobName, Stream content, string contentType, CancellationToken ct = default) =>
        throw NotConfigured();

    public Task<BlobDownload?> OpenReadAsync(string container, string blobName, CancellationToken ct = default) =>
        throw NotConfigured();

    public Task DeleteAsync(string container, string blobName, CancellationToken ct = default) =>
        throw NotConfigured();
}
