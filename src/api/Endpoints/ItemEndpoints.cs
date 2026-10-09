using System.Security.Claims;
using Expenses.Api.Auth;
using Expenses.Api.Data;
using Expenses.Api.Domain;
using Expenses.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Endpoints;

// The household item database (SPEC feature 3). Manual entry and receipt capture build it up; these
// endpoints let parents view, edit, picture, and merge items. Everyone can read; only parents write.
public static class ItemEndpoints
{
    public record ReceiptDescriptionResponse(string Merchant, string PrintedDescription);

    public record ItemResponse(
        Guid Id, string FullName,
        Guid? DefaultCategoryId, string? DefaultCategory,
        Guid? DefaultValueTagId, string? DefaultValueTag,
        bool HasPicture,
        List<ReceiptDescriptionResponse> ReceiptDescriptions);

    // Desired editable state of an item: the web app shows the current values and submits them all.
    // A null DefaultCategoryId or blank DefaultValueTag clears that default.
    public record UpdateItemRequest(string FullName, Guid? DefaultCategoryId, string? DefaultValueTag);

    public record MergeItemRequest(Guid SourceItemId);

    // The most recent purchase of an item, for the "last price / % change" banner during entry.
    public record LastPriceResponse(string FullName, decimal UnitPrice, decimal Amount, decimal Quantity, DateOnly Date, string Merchant);

    public static void MapItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/items").RequireAuthorization();

        // List items, optionally filtered by a full-name search (used by the receipt "pick an existing
        // item" picker). Readable by all household members.
        group.MapGet("/", async (string? search, ClaimsPrincipal user, ExpensesDbContext db, CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            var query = db.Items.AsNoTracking().Where(i => i.HouseholdId == householdId);
            if (!string.IsNullOrWhiteSpace(search))
            {
                // ToLower keeps this case-insensitive on both SQL Server (LOWER) and the in-memory
                // test provider; phase-1 item counts are small, so a contains-scan is fine.
                var term = search.Trim().ToLower();
                query = query.Where(i => i.FullName.ToLower().Contains(term));
            }

            var items = await query.OrderBy(i => i.FullName).Take(500).ToListAsync(ct);
            return Results.Ok(await ToResponses(db, householdId, items, ct));
        });

        // The most recent purchase of an item by full name, so the entry form can show the last price and
        // the % change. 204 when the item is unknown or has no priced history. Readable by all members.
        group.MapGet("/last-price", async (string? name, ClaimsPrincipal user, ExpensesDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.NoContent();
            }
            var householdId = user.GetHouseholdId();
            var trimmed = name.Trim().ToLower();
            var item = await db.Items.AsNoTracking()
                .FirstOrDefaultAsync(i => i.HouseholdId == householdId && i.FullName.ToLower() == trimmed, ct);
            if (item is null)
            {
                return Results.NoContent();
            }

            var last = await (from l in db.LineItems.AsNoTracking()
                              join e in db.Expenses.AsNoTracking() on l.ExpenseId equals e.Id
                              where l.ItemId == item.Id && e.HouseholdId == householdId
                              orderby e.Date descending, e.CreatedAt descending
                              select new { l.UnitPrice, l.Amount, l.Quantity, e.Date, e.MerchantId }).FirstOrDefaultAsync(ct);
            if (last is null)
            {
                return Results.NoContent();
            }

            var merchant = await db.Merchants.AsNoTracking()
                .Where(m => m.Id == last.MerchantId).Select(m => m.Name).FirstOrDefaultAsync(ct);
            return Results.Ok(new LastPriceResponse(item.FullName, last.UnitPrice, last.Amount, last.Quantity, last.Date, merchant ?? ""));
        });

        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, ExpensesDbContext db, CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.HouseholdId == householdId, ct);
            if (item is null)
            {
                return Results.NotFound();
            }
            var responses = await ToResponses(db, householdId, [item], ct);
            return Results.Ok(responses[0]);
        });

        // Rename, change the default category, and set/clear the default value tag. Parent only.
        group.MapPatch("/{id:guid}", async (
            Guid id, UpdateItemRequest request, ClaimsPrincipal user, ExpensesDbContext db, ItemCatalog catalog, CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id && i.HouseholdId == householdId, ct);
            if (item is null)
            {
                return Results.NotFound();
            }

            var name = request.FullName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["fullName"] = ["Name is required."] });
            }
            if (await db.Items.AnyAsync(i => i.HouseholdId == householdId && i.FullName == name && i.Id != id, ct))
            {
                return Results.Conflict(new { message = $"An item named \"{name}\" already exists." });
            }

            if (request.DefaultCategoryId is Guid categoryId &&
                !await db.Categories.AnyAsync(c => c.Id == categoryId && c.HouseholdId == householdId, ct))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["defaultCategoryId"] = ["That category is not in your household."] });
            }

            item.FullName = name;
            item.DefaultCategoryId = request.DefaultCategoryId;
            var tag = await catalog.GetOrCreateValueTagAsync(householdId, user.GetMemberId(), request.DefaultValueTag, ct);
            item.DefaultValueTagId = tag?.Id;

            await db.SaveChangesAsync(ct);
            var responses = await ToResponses(db, householdId, [item], ct);
            return Results.Ok(responses[0]);
        }).RequireAuthorization(AppClaims.ParentPolicy);

        // Upload a photo of the actual item. Resized and re-encoded as JPEG before storage. Parent only.
        group.MapPost("/{id:guid}/picture", async (
            Guid id, HttpRequest http, ClaimsPrincipal user, ExpensesDbContext db, IBlobStorage blobs, CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id && i.HouseholdId == householdId, ct);
            if (item is null)
            {
                return Results.NotFound();
            }
            if (!http.HasFormContentType)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Upload the picture as multipart/form-data."] });
            }

            var form = await http.ReadFormAsync(ct);
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Choose an image to upload."] });
            }
            if (file.Length > ImageValidation.MaxBytes)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["That image is too large (max 10 MB)."] });
            }

            // Read the upload into memory (bounded above), confirm it is really an image, then store it.
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

            var blobName = $"{item.Id}{ImageValidation.ExtensionFor(contentType)}";
            buffer.Position = 0;
            await blobs.UploadAsync(BlobContainers.ItemPictures, blobName, buffer, contentType, ct);

            // A new upload may use a different extension than a previous one; drop the stale blob.
            if (item.PictureBlobName is not null && item.PictureBlobName != blobName)
            {
                await blobs.DeleteAsync(BlobContainers.ItemPictures, item.PictureBlobName, ct);
            }
            item.PictureBlobName = blobName;
            await db.SaveChangesAsync(ct);
            var responses = await ToResponses(db, householdId, [item], ct);
            return Results.Ok(responses[0]);
        }).RequireAuthorization(AppClaims.ParentPolicy);

        // Stream the item's picture back (blobs are private). Readable by all household members.
        group.MapGet("/{id:guid}/picture", async (Guid id, ClaimsPrincipal user, ExpensesDbContext db, IBlobStorage blobs, CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.HouseholdId == householdId, ct);
            if (item?.PictureBlobName is null)
            {
                return Results.NotFound();
            }
            var download = await blobs.OpenReadAsync(BlobContainers.ItemPictures, item.PictureBlobName, ct);
            return download is null ? Results.NotFound() : Results.Stream(download.Content, download.ContentType);
        });

        // Merge one item into another (e.g. the same eggs printed two ways). The source's receipt
        // descriptions and line-item links move to the target, then the source is deleted. Parent only.
        group.MapPost("/{id:guid}/merge", async (
            Guid id, MergeItemRequest request, ClaimsPrincipal user, ExpensesDbContext db, CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            if (request.SourceItemId == id)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["sourceItemId"] = ["An item cannot be merged into itself."] });
            }

            var target = await db.Items.FirstOrDefaultAsync(i => i.Id == id && i.HouseholdId == householdId, ct);
            var source = await db.Items.FirstOrDefaultAsync(i => i.Id == request.SourceItemId && i.HouseholdId == householdId, ct);
            if (target is null || source is null)
            {
                return Results.NotFound();
            }

            // (MerchantId, PrintedDescription) is globally unique, so re-pointing a description to the
            // target can never collide with one the target already has.
            var descriptions = await db.ItemReceiptDescriptions.Where(d => d.ItemId == source.Id).ToListAsync(ct);
            foreach (var d in descriptions)
            {
                d.ItemId = target.Id;
            }

            var lines = await db.LineItems.Where(l => l.ItemId == source.Id).ToListAsync(ct);
            foreach (var l in lines)
            {
                l.ItemId = target.Id;
            }

            // Keep the target's picture and default tag; only adopt the source's if the target has none.
            if (target.PictureBlobName is null && source.PictureBlobName is not null)
            {
                target.PictureBlobName = source.PictureBlobName;
                source.PictureBlobName = null; // so the target keeps the blob after the source row is removed
            }
            target.DefaultValueTagId ??= source.DefaultValueTagId;
            target.DefaultCategoryId ??= source.DefaultCategoryId;

            db.Items.Remove(source);
            await db.SaveChangesAsync(ct);

            var responses = await ToResponses(db, householdId, [target], ct);
            return Results.Ok(responses[0]);
        }).RequireAuthorization(AppClaims.ParentPolicy);
    }

    private static async Task<List<ItemResponse>> ToResponses(ExpensesDbContext db, Guid householdId, List<Item> items, CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking().Where(c => c.HouseholdId == householdId).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var tags = await db.ValueTags.AsNoTracking().Where(t => t.HouseholdId == householdId).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var merchants = await db.Merchants.AsNoTracking().Where(m => m.HouseholdId == householdId).ToDictionaryAsync(m => m.Id, m => m.Name, ct);

        var itemIds = items.Select(i => i.Id).ToList();
        var descriptions = await db.ItemReceiptDescriptions.AsNoTracking()
            .Where(d => itemIds.Contains(d.ItemId))
            .ToListAsync(ct);

        string? Lookup(Dictionary<Guid, string> map, Guid? id) => id is Guid g && map.TryGetValue(g, out var v) ? v : null;

        return items.Select(i => new ItemResponse(
            i.Id,
            i.FullName,
            i.DefaultCategoryId,
            Lookup(categories, i.DefaultCategoryId),
            i.DefaultValueTagId,
            Lookup(tags, i.DefaultValueTagId),
            i.PictureBlobName is not null,
            descriptions.Where(d => d.ItemId == i.Id)
                .Select(d => new ReceiptDescriptionResponse(Lookup(merchants, d.MerchantId) ?? "(unknown)", d.PrintedDescription))
                .OrderBy(r => r.Merchant)
                .ToList()))
            .ToList();
    }
}
