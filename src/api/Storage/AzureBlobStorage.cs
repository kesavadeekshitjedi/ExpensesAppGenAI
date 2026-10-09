using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Expenses.Api.Storage;

// Azure Blob Storage implementation. The BlobServiceClient is built with the managed-identity
// credential in Program.cs (no account key — the account has shared-key access disabled).
public sealed class AzureBlobStorage(BlobServiceClient service) : IBlobStorage
{
    public async Task UploadAsync(string container, string blobName, Stream content, string contentType, CancellationToken ct = default)
    {
        var client = service.GetBlobContainerClient(container);
        var blob = client.GetBlobClient(blobName);
        await blob.UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        }, ct);
    }

    public async Task<BlobDownload?> OpenReadAsync(string container, string blobName, CancellationToken ct = default)
    {
        var blob = service.GetBlobContainerClient(container).GetBlobClient(blobName);
        try
        {
            var response = await blob.DownloadStreamingAsync(cancellationToken: ct);
            var contentType = response.Value.Details.ContentType ?? "application/octet-stream";
            return new BlobDownload(response.Value.Content, contentType);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string container, string blobName, CancellationToken ct = default)
    {
        var blob = service.GetBlobContainerClient(container).GetBlobClient(blobName);
        await blob.DeleteIfExistsAsync(cancellationToken: ct);
    }
}
