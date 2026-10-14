using System.Security.Claims;
using System.Security.Cryptography;
using Expenses.Api.Auth;
using Expenses.Api.Data;
using Expenses.Api.Invoices;
using Expenses.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Endpoints;

// Invoice import (Amazon/Costco order files): a parent uploads an invoice PDF, the API stores it, reads
// its text with Document Intelligence, and parses it into a draft to review. Dedup runs here so the same
// order is not imported twice — by the vendor order number (stable across re-downloads) and by the file
// content hash — and each line is flagged if an item with the same description was already recorded on
// the same date. Nothing is written to the database here except the stored file; the reviewed draft is
// saved through POST /expenses with Source="Invoice".
public static class InvoiceEndpoints
{
    public record ImportLineResponse(
        string PrintedDescription,
        decimal Quantity,
        decimal UnitPrice,
        decimal Amount,
        bool Matched,
        Guid? ItemId,
        string? ItemFullName,
        Guid? SuggestedCategoryId,
        string? SuggestedValueTag,
        bool PossiblyDuplicate,
        Guid? DuplicateExpenseId);

    public record DuplicateInfo(
        Guid ExpenseId,
        DateTimeOffset ImportedAt,
        string Vendor,
        string? OrderNumber,
        decimal GrandTotal,
        DateOnly Date,
        string Reason);

    public record ImportResponse(
        string InvoiceBlobName,
        string ContentHash,
        string Vendor,
        string? OrderNumber,
        DateOnly? Date,
        decimal? Subtotal,
        decimal? Tax,
        decimal? GrandTotal,
        decimal LineSum,
        decimal Difference,
        DuplicateInfo? Duplicate,
        List<ImportLineResponse> Lines);

    public static void MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/invoices").RequireAuthorization();

        group.MapPost("/import", async (
            HttpRequest http, ClaimsPrincipal user, ExpensesDbContext db,
            IBlobStorage blobs, IInvoiceTextReader reader, IEnumerable<IInvoiceParser> parsers,
            CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            if (!http.HasFormContentType)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Upload the invoice as multipart/form-data."] });
            }

            var form = await http.ReadFormAsync(ct);
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Choose an invoice PDF to upload."] });
            }
            if (file.Length > ImageValidation.MaxBytes)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["That file is too large (max 10 MB)."] });
            }

            using var buffer = new MemoryStream();
            await using (var incoming = file.OpenReadStream())
            {
                await incoming.CopyToAsync(buffer, ct);
            }
            var bytes = buffer.ToArray();
            if (!IsPdf(bytes))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Only PDF invoices are supported right now."] });
            }

            var contentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            // Keep the original file (retained indefinitely, alongside receipts).
            var blobName = $"{Guid.NewGuid()}.pdf";
            buffer.Position = 0;
            await blobs.UploadAsync(BlobContainers.Receipts, blobName, buffer, "application/pdf", ct);

            string text;
            try
            {
                text = await reader.ReadTextAsync(BinaryData.FromBytes(bytes), ct);
            }
            catch (Exception)
            {
                // The file is saved; let the user enter the lines by hand against this blob.
                return Results.Ok(new ImportResponse(blobName, contentHash, "Unknown", null, null, null, null, null, 0m, 0m, null, []));
            }

            var parsed = parsers.Select(p => p.TryParse(text)).FirstOrDefault(r => r is not null);
            if (parsed is null)
            {
                return Results.Ok(new ImportResponse(blobName, contentHash, "Unknown", null, null, null, null, null, 0m, 0m, null, []));
            }

            // Invoice-level dedup: same order number (primary) or identical file bytes (secondary).
            var duplicate = await FindDuplicateAsync(db, householdId, parsed.OrderNumber, contentHash, ct);

            // Item-level dedup: descriptions already recorded on the same purchase date.
            var sameDayDescriptions = parsed.OrderDate is DateOnly date
                ? await SameDateDescriptionsAsync(db, householdId, date, ct)
                : [];

            var matchIndex = await BuildMatchIndex(db, householdId, parsed.Vendor, ct);

            var lines = new List<ImportLineResponse>();
            decimal lineSum = 0;
            foreach (var line in parsed.Lines)
            {
                lineSum += line.Amount;
                matchIndex.TryGetValue(Normalize(line.Description), out var match);
                sameDayDescriptions.TryGetValue(Normalize(line.Description), out var dupExpenseId);
                lines.Add(new ImportLineResponse(
                    line.Description,
                    line.Quantity ?? 1m,
                    line.UnitPrice ?? line.Amount,
                    line.Amount,
                    match is not null,
                    match?.ItemId,
                    match?.FullName,
                    match?.DefaultCategoryId,
                    match?.DefaultValueTag,
                    dupExpenseId != Guid.Empty,
                    dupExpenseId == Guid.Empty ? null : dupExpenseId));
            }

            var difference = (parsed.GrandTotal ?? lineSum) - lineSum;
            return Results.Ok(new ImportResponse(
                blobName, contentHash, parsed.Vendor, parsed.OrderNumber, parsed.OrderDate,
                parsed.Subtotal, parsed.Tax, parsed.GrandTotal, lineSum, difference, duplicate, lines));
        }).RequireAuthorization(AppClaims.ParentPolicy);
    }

    private static async Task<DuplicateInfo?> FindDuplicateAsync(
        ExpensesDbContext db, Guid householdId, string? orderNumber, string contentHash, CancellationToken ct)
    {
        var existing = await db.ImportedInvoices
            .Where(i => i.HouseholdId == householdId
                && ((orderNumber != null && i.OrderNumber == orderNumber) || i.ContentHash == contentHash))
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            return null;
        }

        var reason = orderNumber != null && existing.OrderNumber == orderNumber
            ? $"An invoice for order {orderNumber} was already imported."
            : "This exact file was already imported.";
        return new DuplicateInfo(
            existing.ExpenseId, existing.CreatedAt, existing.Vendor, existing.OrderNumber,
            existing.GrandTotal, existing.PurchaseDate, reason);
    }

    // Normalized printed descriptions of line items whose expense is dated on the given day, mapped to
    // one expense that contains them (so a re-imported order's overlapping lines can be flagged).
    private static async Task<Dictionary<string, Guid>> SameDateDescriptionsAsync(
        ExpensesDbContext db, Guid householdId, DateOnly date, CancellationToken ct)
    {
        var rows = await (from l in db.LineItems.AsNoTracking()
                          join e in db.Expenses.AsNoTracking() on l.ExpenseId equals e.Id
                          where e.HouseholdId == householdId && e.Date == date
                          select new { l.Description, e.Id })
                         .ToListAsync(ct);

        var map = new Dictionary<string, Guid>();
        foreach (var row in rows)
        {
            map[Normalize(row.Description)] = row.Id;
        }
        return map;
    }

    private record Match(Guid ItemId, string FullName, Guid? DefaultCategoryId, string? DefaultValueTag);

    // Maps a vendor merchant's printed descriptions (normalized) to the item they resolve to, so a
    // re-seen item is recognized. No merchant match => every line is "unmatched" (asks the user).
    private static async Task<Dictionary<string, Match>> BuildMatchIndex(
        ExpensesDbContext db, Guid householdId, string vendor, CancellationToken ct)
    {
        var index = new Dictionary<string, Match>();
        var merchant = await db.Merchants.FirstOrDefaultAsync(m => m.HouseholdId == householdId && m.Name == vendor, ct);
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
            string? tagName = row.DefaultValueTagId is Guid tid && tags.TryGetValue(tid, out var tn) ? tn : null;
            index[Normalize(row.PrintedDescription)] = new Match(row.Id, row.FullName, row.DefaultCategoryId, tagName);
        }
        return index;
    }

    private static bool IsPdf(byte[] bytes) =>
        bytes.Length >= 5 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46 && bytes[4] == 0x2D;

    private static string Normalize(string s) => s.Trim().ToUpperInvariant();
}
