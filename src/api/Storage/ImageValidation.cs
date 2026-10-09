namespace Expenses.Api.Storage;

// Lightweight, dependency-free checks for uploaded item pictures and receipt images. We store the
// original bytes (no server-side resize in phase 1 — SPEC's resize is a proposed optimization; a
// cross-platform resizer such as SkiaSharp can be added later), so we just bound the size and sniff
// the first bytes to confirm it is a common image format before keeping it.
public static class ImageValidation
{
    public const long MaxBytes = 10 * 1024 * 1024; // 10 MB — ample for a phone photo

    // Returns the content type for a recognized image, or null if the bytes are not a supported image.
    public static string? SniffContentType(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
        {
            return "image/jpeg";
        }
        if (head.Length >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47)
        {
            return "image/png";
        }
        if (head.Length >= 12 && head[0] == 0x52 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x46
            && head[8] == 0x57 && head[9] == 0x45 && head[10] == 0x42 && head[11] == 0x50)
        {
            return "image/webp"; // "RIFF"...."WEBP"
        }
        if (head.Length >= 3 && head[0] == 0x47 && head[1] == 0x49 && head[2] == 0x46)
        {
            return "image/gif";
        }
        return null;
    }

    public static string ExtensionFor(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ => ".jpg",
    };
}
