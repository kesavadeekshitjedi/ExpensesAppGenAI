using System.Security.Claims;
using Expenses.Api.Auth;
using Expenses.Api.Data;
using Expenses.Api.Receipts;
using Expenses.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Endpoints;

// Receipt capture (SPEC feature 3): a parent photographs a receipt, the API stores the image and
// reads its text, then matches each printed line against the household item database so the web app
// can show a draft to review. Nothing is written to the database here — only the image is stored; the
// reviewed draft is saved through POST /expenses with Source="Receipt".
public static class ReceiptEndpoints
{
    public record ScanLineResponse(
        string PrintedDescription,
        decimal Quantity,
        decimal UnitPrice,
        decimal Amount,
        bool Matched,
        Guid? ItemId,
        string? ItemFullName,
        Guid? SuggestedCategoryId,
        string? SuggestedValueTag);

    public record ScanResponse(
        string ReceiptBlobName,
        string? Merchant,
        DateOnly? Date,
        decimal? Total,
        decimal? Tax,
        decimal LineSum,
        decimal Difference,
        List<ScanLineResponse> Lines);

    public static void MapReceiptEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/receipts").RequireAuthorization();

        // Scan a receipt image into a draft. Parent only (children are view-only).
        group.MapPost("/scan", async (
            HttpRequest http, ClaimsPrincipal user, ExpensesDbContext db,
            IBlobStorage blobs, IReceiptReader reader, CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            if (!http.HasFormContentType)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Upload the receipt as multipart/form-data."] });
            }

            var form = await http.ReadFormAsync(ct);
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Choose a receipt image to upload."] });
            }
            if (file.Length > ImageValidation.MaxBytes)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["That image is too large (max 10 MB)."] });
            }

            using var buffer = new MemoryStream();
            await using (var incoming = file.OpenReadStream())
            {
                await incoming.CopyToAsync(buffer, ct);
            }
            var contentType = ImageValidation.SniffContentType(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 16)));
            if (contentType is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["That file is not a supported image (JPEG, PNG, WebP, or GIF)."] });
            }

            // Keep the original image (retained indefinitely — SPEC Open Question #6).
            var blobName = $"{Guid.NewGuid()}{ImageValidation.ExtensionFor(contentType)}";
            buffer.Position = 0;
            await blobs.UploadAsync(BlobContainers.Receipts, blobName, buffer, contentType, ct);

            ExtractedReceipt extracted;
            try
            {
                extracted = await reader.AnalyzeAsync(BinaryData.FromBytes(buffer.ToArray()), ct);
            }
            catch (Exception)
            {
                // The image is saved; let the user enter the lines by hand against this blob.
                return Results.Ok(new ScanResponse(blobName, null, null, null, null, 0m, 0m, []));
            }

            // Match printed lines against this merchant's known receipt descriptions, if the merchant
            // has been seen before. No merchant match => every line is "unmatched" (asks the user).
            var matchIndex = await BuildMatchIndex(db, householdId, extracted.Merchant, ct);

            var lines = new List<ScanLineResponse>();
            decimal lineSum = 0;
            foreach (var line in extracted.Lines)
            {
                var quantity = line.Quantity is > 0 ? line.Quantity.Value : 1m;
                var unitPrice = line.UnitPrice ?? 0m;
                var amount = line.Amount ?? decimal.Round(quantity * unitPrice, 2);
                lineSum += amount;

                matchIndex.TryGetValue(Normalize(line.Description), out var match);
                lines.Add(new ScanLineResponse(
                    line.Description,
                    quantity,
                    unitPrice,
                    amount,
                    match is not null,
                    match?.ItemId,
                    match?.FullName,
                    match?.DefaultCategoryId,
                    match?.DefaultValueTag));
            }

            var difference = (extracted.Total ?? lineSum) - lineSum;
            return Results.Ok(new ScanResponse(
                blobName, extracted.Merchant, extracted.Date, extracted.Total, extracted.Tax, lineSum, difference, lines));
        }).RequireAuthorization(AppClaims.ParentPolicy);
    }

    private record Match(Guid ItemId, string FullName, Guid? DefaultCategoryId, string? DefaultValueTag);

    // Maps a merchant's printed descriptions (normalized) to the item they resolve to.
    private static async Task<Dictionary<string, Match>> BuildMatchIndex(
        ExpensesDbContext db, Guid householdId, string? merchantName, CancellationToken ct)
    {
        var index = new Dictionary<string, Match>();
        if (string.IsNullOrWhiteSpace(merchantName))
        {
            return index;
        }

        var name = merchantName.Trim();
        var merchant = await db.Merchants.FirstOrDefaultAsync(m => m.HouseholdId == householdId && m.Name == name, ct);
        if (merchant is null)
        {
            return index;
        }

        var tags = await db.ValueTags.AsNoTracking().Where(t => t.HouseholdId == householdId).ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var rows = await (from d in db.ItemReceiptDescriptions.AsNoTracking()
                          join i in db.Items.AsNoTracking() on d.ItemId equals i.Id
                          where d.MerchantId == merchant.Id && i.HouseholdId == householdId
                          select new { d.PrintedDescription, i.Id, i.FullName, i.DefaultCategoryId, i.DefaultValueTagId })
                         .ToListAsync(ct);

        foreach (var row in rows)
        {
            var key = Normalize(row.PrintedDescription);
            string? tagName = row.DefaultValueTagId is Guid tid && tags.TryGetValue(tid, out var tn) ? tn : null;
            index[key] = new Match(row.Id, row.FullName, row.DefaultCategoryId, tagName);
        }
        return index;
    }

    private static string Normalize(string s) => s.Trim().ToUpperInvariant();
}
