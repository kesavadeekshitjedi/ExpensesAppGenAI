using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Data;

// Keeps the household item database (SPEC feature 3) in step with manual expense entry. Given the
// full name the user typed for a line, it reuses or creates the matching Item and makes sure the
// merchant has a receipt "short form" for it — figuring the short form out when the user doesn't
// supply one. Entities are added to the context but not saved here; the caller owns the transaction.
public class ItemCatalog(ExpensesDbContext db)
{
    public record Resolved(Item Item, string ShortForm);

    public async Task<Resolved> ResolveAsync(
        Guid householdId,
        Guid merchantId,
        string fullName,
        Guid? defaultCategoryId,
        string? explicitShortForm,
        CancellationToken ct = default)
    {
        fullName = fullName.Trim();

        // Reuse an existing item with the same full name; new items are tracked so a brand-new item
        // and its first receipt description are inserted together when the caller saves.
        var trackedNew = db.Items.Local.FirstOrDefault(i => i.HouseholdId == householdId && i.FullName == fullName);
        var item = trackedNew
            ?? await db.Items.FirstOrDefaultAsync(i => i.HouseholdId == householdId && i.FullName == fullName, ct);
        if (item is null)
        {
            item = new Item
            {
                Id = Guid.NewGuid(),
                HouseholdId = householdId,
                FullName = fullName,
                DefaultCategoryId = defaultCategoryId,
            };
            db.Items.Add(item);
        }

        // Does this merchant already have a printed form for the item? If so, reuse it.
        var existing = db.ItemReceiptDescriptions.Local.FirstOrDefault(d => d.ItemId == item.Id && d.MerchantId == merchantId)
            ?? await db.ItemReceiptDescriptions.FirstOrDefaultAsync(d => d.ItemId == item.Id && d.MerchantId == merchantId, ct);
        if (existing is not null)
        {
            return new Resolved(item, existing.PrintedDescription);
        }

        var candidate = string.IsNullOrWhiteSpace(explicitShortForm)
            ? ShortForm.FromFullName(fullName)
            : explicitShortForm.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(candidate))
        {
            candidate = "ITEM";
        }
        candidate = await MakeUniqueForMerchantAsync(merchantId, candidate, ct);

        db.ItemReceiptDescriptions.Add(new ItemReceiptDescription
        {
            Id = Guid.NewGuid(),
            ItemId = item.Id,
            MerchantId = merchantId,
            PrintedDescription = candidate,
        });

        return new Resolved(item, candidate);
    }

    // The (merchant, printed description) pair is unique, so if a different item already claims this
    // short form at this merchant, suffix it (" 2", " 3", ...) until it is free.
    private async Task<string> MakeUniqueForMerchantAsync(Guid merchantId, string candidate, CancellationToken ct)
    {
        var taken = await db.ItemReceiptDescriptions
            .Where(d => d.MerchantId == merchantId)
            .Select(d => d.PrintedDescription)
            .ToListAsync(ct);
        var local = db.ItemReceiptDescriptions.Local
            .Where(d => d.MerchantId == merchantId)
            .Select(d => d.PrintedDescription);
        var used = new HashSet<string>(taken.Concat(local), StringComparer.OrdinalIgnoreCase);

        if (!used.Contains(candidate))
        {
            return candidate;
        }

        for (var n = 2; ; n++)
        {
            var suffixed = $"{candidate} {n}";
            if (!used.Contains(suffixed))
            {
                return suffixed;
            }
        }
    }
}
