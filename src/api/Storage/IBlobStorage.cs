namespace Expenses.Api.Storage;

// Stores and retrieves binary blobs (item pictures, receipt images) in named containers. Backed by
// Azure Blob Storage in Azure (reached through the managed identity); a no-op stand-in is used when
// storage is not configured, and tests supply an in-memory fake.
public interface IBlobStorage
{
    Task UploadAsync(string container, string blobName, Stream content, string contentType, CancellationToken ct = default);

    // Opens the blob for reading, or returns null if it does not exist.
    Task<BlobDownload?> OpenReadAsync(string container, string blobName, CancellationToken ct = default);

    Task DeleteAsync(string container, string blobName, CancellationToken ct = default);
}

public sealed record BlobDownload(Stream Content, string ContentType);

public static class BlobContainers
{
    public const string ItemPictures = "item-pictures";
    public const string Receipts = "receipts";
}
