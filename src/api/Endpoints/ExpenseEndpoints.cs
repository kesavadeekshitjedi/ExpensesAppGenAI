using System.Security.Claims;
using Expenses.Api.Auth;
using Expenses.Api.Data;
using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Endpoints;

public static class ExpenseEndpoints
{
    public record CreateLineItemRequest(
        string Description,      // manual: the item's full name; receipt: the printed text
        Guid CategoryId,
        Guid? ForMemberId,       // null = Family
        decimal? Quantity,
        decimal? UnitPrice,
        decimal? Amount,         // if null, computed from quantity x unit price
        string? ValueTag,
        string? Notes,
        string? ShortForm,       // manual only; the app figures one out when blank
        Guid? ItemId = null,     // receipt: a chosen existing item for this printed line
        string? FullName = null, // receipt: a new item's full name for this printed line
        Guid? VehicleId = null); // the vehicle this line is a cost for (e.g. gas), or null

    public record CreateExpenseRequest(
        string Merchant,
        Guid PaymentMethodId,
        DateOnly? Date,
        decimal? Tax,
        string? Notes,
        List<CreateLineItemRequest> LineItems,
        string? Source = null,            // "Receipt" for a reviewed receipt scan; otherwise manual
        string? ReceiptBlobName = null);  // the stored receipt image (receipt source only)

    public record LineItemResponse(
        Guid Id, string Description, Guid CategoryId, string Category, Guid? ForMemberId, string For,
        decimal Quantity, decimal UnitPrice, decimal Amount, decimal AllocatedTax, string? ValueTag, string? Notes,
        Guid? ItemId, string? ShortForm, Guid? VehicleId, string? Vehicle);

    public record ExpenseResponse(
        Guid Id, string Merchant, Guid PaymentMethodId, string PaymentMethod, DateOnly Date,
        decimal Total, decimal? Tax, string? Notes, string Source, string EnteredBy,
        List<LineItemResponse> LineItems);

    public static void MapExpenseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/expenses").RequireAuthorization();

        // Everyone in the household (including view-only children) can read expenses.
        group.MapGet("/", async (ClaimsPrincipal user, ExpensesDbContext db, CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            var expenses = await LoadExpenses(db, householdId)
                .OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt)
                .Take(200)
                .ToListAsync(ct);
            return Results.Ok(await ToResponses(db, householdId, expenses, ct));
        });

        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, ExpensesDbContext db, CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            var expense = await LoadExpenses(db, householdId).FirstOrDefaultAsync(e => e.Id == id, ct);
            if (expense is null)
            {
                return Results.NotFound();
            }
            var responses = await ToResponses(db, householdId, [expense], ct);
            return Results.Ok(responses[0]);
        });

        // Enter an expense. Parent only (children are view-only). The merchant, item database and any
        // new value tags are created as needed; the total is the sum of the line amounts.
        group.MapPost("/", async (
            CreateExpenseRequest request,
            ClaimsPrincipal user,
            ExpensesDbContext db,
            ItemCatalog catalog,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(request.Merchant))
            {
                errors["merchant"] = ["Merchant is required."];
            }
            if (request.LineItems is null || request.LineItems.Count == 0)
            {
                errors["lineItems"] = ["Add at least one line item."];
            }

            var paymentMethod = await db.PaymentMethods
                .FirstOrDefaultAsync(p => p.Id == request.PaymentMethodId && p.HouseholdId == householdId, ct);
            if (paymentMethod is null)
            {
                errors["paymentMethodId"] = ["Choose a payment method."];
            }

            // Load the household's reference names once up front. These are reused both to validate the
            // request and to build the response, so the save path makes far fewer round-trips to the
            // (Basic-tier, latency-sensitive) database than re-querying everything after SaveChanges.
            var categoryRows = await db.Categories.AsNoTracking()
                .Where(c => c.HouseholdId == householdId)
                .Select(c => new { c.Id, c.Name, c.IsTaxable })
                .ToListAsync(ct);
            var categoryNames = categoryRows.ToDictionary(c => c.Id, c => c.Name);
            var taxableCategoryIds = categoryRows.Where(c => c.IsTaxable).Select(c => c.Id).ToHashSet();
            var memberNames = await db.Members.AsNoTracking()
                .Where(m => m.HouseholdId == householdId)
                .ToDictionaryAsync(m => m.Id, m => m.DisplayName, ct);
            var valueTagNames = await db.ValueTags.AsNoTracking()
                .Where(t => t.HouseholdId == householdId)
                .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
            var vehicleNames = await db.Vehicles.AsNoTracking()
                .Where(v => v.HouseholdId == householdId)
                .ToDictionaryAsync(v => v.Id, v => v.Name, ct);

            for (var i = 0; request.LineItems is not null && i < request.LineItems.Count; i++)
            {
                var line = request.LineItems[i];
                if (string.IsNullOrWhiteSpace(line.Description))
                {
                    errors[$"lineItems[{i}].description"] = ["Description is required."];
                }
                if (!categoryNames.ContainsKey(line.CategoryId))
                {
                    errors[$"lineItems[{i}].categoryId"] = ["Choose a category for this line."];
                }
                if (line.ForMemberId is Guid forId && !memberNames.ContainsKey(forId))
                {
                    errors[$"lineItems[{i}].forMemberId"] = ["That member is not in your household."];
                }
                if (line.VehicleId is Guid vehId && !vehicleNames.ContainsKey(vehId))
                {
                    errors[$"lineItems[{i}].vehicleId"] = ["That vehicle is not in your household."];
                }
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var merchant = await GetOrCreateMerchant(db, householdId, request.Merchant.Trim(), ct);
            var isReceipt = string.Equals(request.Source, nameof(ExpenseSource.Receipt), StringComparison.OrdinalIgnoreCase);

            var expense = new Expense
            {
                Id = Guid.NewGuid(),
                HouseholdId = householdId,
                MerchantId = merchant.Id,
                PaymentMethodId = paymentMethod!.Id,
                EnteredByMemberId = user.GetMemberId(),
                Date = request.Date ?? DateOnly.FromDateTime(clock.GetUtcNow().LocalDateTime),
                Tax = request.Tax,
                Notes = Trimmed(request.Notes),
                Source = isReceipt ? ExpenseSource.Receipt : ExpenseSource.Manual,
            };

            // Remember each line's display bits (short form, value-tag name) as we resolve them, so the
            // response is built from memory instead of a second set of queries.
            var lineShortForms = new Dictionary<Guid, string?>();
            var lineTagNames = new Dictionary<Guid, string?>();

            decimal total = 0;
            foreach (var line in request.LineItems!)
            {
                var quantity = line.Quantity is > 0 ? line.Quantity.Value : 1m;
                var unitPrice = line.UnitPrice ?? 0m;
                var amount = line.Amount ?? decimal.Round(quantity * unitPrice, 2);
                total += amount;

                // Manual entry: the description is the full name, so figure out a short form. Receipt:
                // the description is the printed text; link it to the chosen/new item as-is.
                Item item;
                string? shortForm;
                if (isReceipt)
                {
                    item = await catalog.ResolveFromReceiptAsync(
                        householdId, merchant.Id, line.Description.Trim(), line.ItemId, line.FullName, line.CategoryId, ct);
                    shortForm = line.Description.Trim();
                }
                else
                {
                    var resolved = await catalog.ResolveAsync(
                        householdId, merchant.Id, line.Description.Trim(), line.CategoryId, line.ShortForm, ct);
                    item = resolved.Item;
                    shortForm = resolved.ShortForm;
                }

                // A typed tag wins; otherwise fall back to the item's default value tag (SPEC feature 4).
                var valueTag = await catalog.GetOrCreateValueTagAsync(householdId, user.GetMemberId(), line.ValueTag, ct);
                var tagId = valueTag?.Id ?? item.DefaultValueTagId;
                var tagName = valueTag?.Name
                    ?? (item.DefaultValueTagId is Guid dt && valueTagNames.TryGetValue(dt, out var n) ? n : null);

                var lineItem = new LineItem
                {
                    Id = Guid.NewGuid(),
                    ExpenseId = expense.Id,
                    Description = line.Description.Trim(),
                    ItemId = item.Id,
                    CategoryId = line.CategoryId,
                    ForMemberId = line.ForMemberId,
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                    Amount = amount,
                    ValueTagId = tagId,
                    VehicleId = line.VehicleId,
                    Notes = Trimmed(line.Notes),
                };
                expense.LineItems.Add(lineItem);
                lineShortForms[lineItem.Id] = string.IsNullOrEmpty(shortForm) ? null : shortForm;
                lineTagNames[lineItem.Id] = tagName;
            }

            expense.Total = total;

            // Spread the expense's sales tax across its taxable lines, so each item's true cost reflects
            // its share of tax (SPEC feature 3). Falls back to all lines if none are in a taxable category.
            if (expense.Tax is decimal taxAmount && taxAmount > 0)
            {
                AllocateTax(expense.LineItems, taxableCategoryIds, taxAmount);
            }

            db.Expenses.Add(expense);
            if (isReceipt && !string.IsNullOrWhiteSpace(request.ReceiptBlobName))
            {
                db.Receipts.Add(new Receipt
                {
                    Id = Guid.NewGuid(),
                    ExpenseId = expense.Id,
                    ImageBlobName = request.ReceiptBlobName.Trim(),
                    ExtractionStatus = ReceiptExtractionStatus.Extracted,
                });
            }
            await db.SaveChangesAsync(ct);

            var response = new ExpenseResponse(
                expense.Id,
                merchant.Name,
                paymentMethod.Id,
                paymentMethod.Label,
                expense.Date,
                expense.Total,
                expense.Tax,
                expense.Notes,
                expense.Source.ToString(),
                memberNames.GetValueOrDefault(expense.EnteredByMemberId, "(unknown)"),
                expense.LineItems.Select(l => new LineItemResponse(
                    l.Id,
                    l.Description,
                    l.CategoryId,
                    categoryNames.GetValueOrDefault(l.CategoryId, "(unknown)"),
                    l.ForMemberId,
                    l.ForMemberId is Guid fm ? memberNames.GetValueOrDefault(fm, "(unknown)") : "Family",
                    l.Quantity,
                    l.UnitPrice,
                    l.Amount,
                    l.AllocatedTax,
                    lineTagNames.GetValueOrDefault(l.Id),
                    l.Notes,
                    l.ItemId,
                    lineShortForms.GetValueOrDefault(l.Id),
                    l.VehicleId,
                    l.VehicleId is Guid lv ? vehicleNames.GetValueOrDefault(lv) : null))
                    .ToList());
            return Results.Created($"/expenses/{expense.Id}", response);
        }).RequireAuthorization(AppClaims.ParentPolicy);
    }

    private static IQueryable<Expense> LoadExpenses(ExpensesDbContext db, Guid householdId) =>
        db.Expenses.AsNoTracking()
            .Where(e => e.HouseholdId == householdId)
            .Include(e => e.LineItems);

    private static async Task<Merchant> GetOrCreateMerchant(ExpensesDbContext db, Guid householdId, string name, CancellationToken ct)
    {
        var merchant = await db.Merchants.FirstOrDefaultAsync(m => m.HouseholdId == householdId && m.Name == name, ct);
        if (merchant is null)
        {
            merchant = new Merchant { Id = Guid.NewGuid(), HouseholdId = householdId, Name = name };
            db.Merchants.Add(merchant);
        }
        return merchant;
    }

    // Builds responses with the human-readable names the web app shows, looked up in bulk.
    private static async Task<List<ExpenseResponse>> ToResponses(ExpensesDbContext db, Guid householdId, List<Expense> expenses, CancellationToken ct)
    {
        var merchants = await db.Merchants.AsNoTracking().Where(m => m.HouseholdId == householdId).ToDictionaryAsync(m => m.Id, m => m.Name, ct);
        var methods = await db.PaymentMethods.AsNoTracking().Where(p => p.HouseholdId == householdId).ToDictionaryAsync(p => p.Id, p => p.Label, ct);
        var categories = await db.Categories.AsNoTracking().Where(c => c.HouseholdId == householdId).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var members = await db.Members.AsNoTracking().Where(m => m.HouseholdId == householdId).ToDictionaryAsync(m => m.Id, m => m.DisplayName, ct);
        var tags = await db.ValueTags.AsNoTracking().Where(t => t.HouseholdId == householdId).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var vehicles = await db.Vehicles.AsNoTracking().Where(v => v.HouseholdId == householdId).ToDictionaryAsync(v => v.Id, v => v.Name, ct);

        var itemIds = expenses.SelectMany(e => e.LineItems).Where(l => l.ItemId is not null).Select(l => l.ItemId!.Value).Distinct().ToList();
        var shortForms = await db.ItemReceiptDescriptions.AsNoTracking()
            .Where(d => itemIds.Contains(d.ItemId))
            .ToListAsync(ct);

        string Name<T>(Dictionary<Guid, T> map, Guid id) => map.TryGetValue(id, out var v) ? v!.ToString()! : "(unknown)";

        return expenses.Select(e => new ExpenseResponse(
            e.Id,
            Name(merchants, e.MerchantId),
            e.PaymentMethodId,
            Name(methods, e.PaymentMethodId),
            e.Date,
            e.Total,
            e.Tax,
            e.Notes,
            e.Source.ToString(),
            Name(members, e.EnteredByMemberId),
            e.LineItems.Select(l => new LineItemResponse(
                l.Id,
                l.Description,
                l.CategoryId,
                Name(categories, l.CategoryId),
                l.ForMemberId,
                l.ForMemberId is Guid fm ? Name(members, fm) : "Family",
                l.Quantity,
                l.UnitPrice,
                l.Amount,
                l.AllocatedTax,
                l.ValueTagId is Guid vt ? Name(tags, vt) : null,
                l.Notes,
                l.ItemId,
                l.ItemId is Guid it
                    ? shortForms.FirstOrDefault(d => d.ItemId == it && d.MerchantId == e.MerchantId)?.PrintedDescription
                    : null,
                l.VehicleId,
                l.VehicleId is Guid lv ? vehicles.GetValueOrDefault(lv) : null))
                .ToList()))
            .ToList();
    }

    // Distributes an expense's tax across its taxable lines, proportional to each line's amount. Works in
    // whole cents and hands the leftover cents to the largest fractional shares, so the allocated amounts
    // always sum back to the tax exactly. Falls back to all lines when none are in a taxable category.
    private static void AllocateTax(ICollection<LineItem> lines, HashSet<Guid> taxableCategoryIds, decimal tax)
    {
        var target = lines.Where(l => taxableCategoryIds.Contains(l.CategoryId)).ToList();
        if (target.Count == 0 || target.Sum(l => l.Amount) == 0m)
        {
            target = lines.ToList();
        }

        var baseSum = target.Sum(l => l.Amount);
        if (baseSum == 0m)
        {
            return; // nothing to weight the allocation by
        }

        var taxCents = (long)Math.Round(tax * 100m, MidpointRounding.AwayFromZero);
        var shares = target
            .Select(l =>
            {
                var exact = taxCents * l.Amount / baseSum;
                var floor = (long)Math.Floor(exact);
                return (line: l, cents: floor, frac: exact - floor);
            })
            .ToList();

        var leftover = taxCents - shares.Sum(s => s.cents);
        var ordered = shares.OrderByDescending(s => s.frac).ThenByDescending(s => s.line.Amount).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var extra = i < leftover ? 1L : 0L;
            ordered[i].line.AllocatedTax = (ordered[i].cents + extra) / 100m;
        }
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
