using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Data;

// Keeps the household item database (SPEC feature 3) in step with expense entry. Given the full name
// the user typed for a manual line, it reuses or creates the matching Item and makes sure the
// merchant has a receipt "short form" for it — figuring the short form out when the user doesn't
// supply one. For receipt capture (step 11) it links the actual printed description instead. Value
// tags are created on first use and reused. Entities are added to the context but not saved here;
// the caller owns the transaction.
public class ItemCatalog(ExpensesDbContext db)
{
    public record Resolved(Item Item, string ShortForm);

    // Manual entry: the user typed a full name; figure out (or accept) the merchant's short form.
    public async Task<Resolved> ResolveAsync(
        Guid householdId,
        Guid merchantId,
        string fullName,
        Guid? defaultCategoryId,
        string? explicitShortForm,
        CancellationToken ct = default)
    {
        var item = await GetOrCreateItemAsync(householdId, fullName, defaultCategoryId, ct);

        // Does this merchant already have a printed form for the item? If so, reuse it.
        var existing = db.ItemReceiptDescriptions.Local.FirstOrDefault(d => d.ItemId == item.Id && d.MerchantId == merchantId)
            ?? await db.ItemReceiptDescriptions.FirstOrDefaultAsync(d => d.ItemId == item.Id && d.MerchantId == merchantId, ct);
        if (existing is not null)
        {
            return new Resolved(item, existing.PrintedDescription);
        }

        var candidate = string.IsNullOrWhiteSpace(explicitShortForm)
            ? ShortForm.FromFullName(item.FullName)
            : explicitShortForm.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(candidate))
        {
            candidate = "ITEM";
        }
        candidate = await MakeUniqueForMerchantAsync(merchantId, candidate, ct);

        AddReceiptDescription(item.Id, merchantId, candidate);
        return new Resolved(item, candidate);
    }

    // Receipt capture: the printed description is already known (it is what the receipt showed). Link
    // it to the chosen existing item (itemId) or to a new/looked-up item by full name, so the same
    // description is recognized automatically next time. Returns the resolved item.
    public async Task<Item> ResolveFromReceiptAsync(
        Guid householdId,
        Guid merchantId,
        string printedDescription,
        Guid? itemId,
        string? fullName,
        Guid? defaultCategoryId,
        CancellationToken ct = default)
    {
        Item item;
        if (itemId is Guid id)
        {
            item = db.Items.Local.FirstOrDefault(i => i.Id == id && i.HouseholdId == householdId)
                ?? await db.Items.FirstOrDefaultAsync(i => i.Id == id && i.HouseholdId == householdId, ct)
                ?? throw new InvalidOperationException("The chosen item is not in this household.");
        }
        else
        {
            var name = string.IsNullOrWhiteSpace(fullName) ? printedDescription : fullName;
            item = await GetOrCreateItemAsync(householdId, name, defaultCategoryId, ct);
        }

        var printed = printedDescription.Trim();
        if (printed.Length > 100)
        {
            printed = printed[..100];
        }

        // If this merchant already maps this printed text (to any item), leave the existing mapping in
        // place; otherwise record it for the resolved item.
        var existingForMerchant = db.ItemReceiptDescriptions.Local
            .FirstOrDefault(d => d.MerchantId == merchantId && string.Equals(d.PrintedDescription, printed, StringComparison.OrdinalIgnoreCase))
            ?? await db.ItemReceiptDescriptions
                .FirstOrDefaultAsync(d => d.MerchantId == merchantId && d.PrintedDescription == printed, ct);
        if (existingForMerchant is null && printed.Length > 0)
        {
            AddReceiptDescription(item.Id, merchantId, printed);
        }

        return item;
    }

    // Finds or creates a value tag by name (unique per household, case-insensitive — SPEC feature 4).
    public async Task<ValueTag?> GetOrCreateValueTagAsync(Guid householdId, Guid memberId, string? name, CancellationToken ct = default)
    {
        name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (name is null)
        {
            return null;
        }

        var existing = db.ValueTags.Local.FirstOrDefault(t => t.HouseholdId == householdId && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? await db.ValueTags.FirstOrDefaultAsync(t => t.HouseholdId == householdId && t.Name == name, ct);
        if (existing is not null)
        {
            return existing;
        }

        var tag = new ValueTag { Id = Guid.NewGuid(), HouseholdId = householdId, Name = name, CreatedByMemberId = memberId };
        db.ValueTags.Add(tag);
        return tag;
    }

    private async Task<Item> GetOrCreateItemAsync(Guid householdId, string fullName, Guid? defaultCategoryId, CancellationToken ct)
    {
        fullName = fullName.Trim();

        // Reuse an existing item with the same full name; new items are tracked so a brand-new item
        // and its first receipt description are inserted together when the caller saves.
        var item = db.Items.Local.FirstOrDefault(i => i.HouseholdId == householdId && i.FullName == fullName)
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
        return item;
    }

    private void AddReceiptDescription(Guid itemId, Guid merchantId, string printedDescription) =>
        db.ItemReceiptDescriptions.Add(new ItemReceiptDescription
        {
            Id = Guid.NewGuid(),
            ItemId = itemId,
            MerchantId = merchantId,
            PrintedDescription = printedDescription,
        });

    // The (merchant, printed description) pair is unique, so if a different item already claims this
    // short form at this merchant, suffix it (" 2", " 3", ...) until it is free.
    public async Task<string> MakeUniqueForMerchantAsync(Guid merchantId, string candidate, CancellationToken ct = default)
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
