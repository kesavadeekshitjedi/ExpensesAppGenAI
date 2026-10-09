using System.Security.Claims;
using Expenses.Api.Auth;
using Expenses.Api.Data;
using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Endpoints;

public static class ExpenseEndpoints
{
    public record CreateLineItemRequest(
        string Description,
        Guid CategoryId,
        Guid? ForMemberId,       // null = Family
        decimal? Quantity,
        decimal? UnitPrice,
        decimal? Amount,         // if null, computed from quantity x unit price
        string? ValueTag,
        string? Notes,
        string? ShortForm);      // optional; the app figures one out when blank

    public record CreateExpenseRequest(
        string Merchant,
        Guid PaymentMethodId,
        DateOnly? Date,
        decimal? Tax,
        string? Notes,
        List<CreateLineItemRequest> LineItems);

    public record LineItemResponse(
        Guid Id, string Description, Guid CategoryId, string Category, Guid? ForMemberId, string For,
        decimal Quantity, decimal UnitPrice, decimal Amount, string? ValueTag, string? Notes,
        Guid? ItemId, string? ShortForm);

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

            // Validate the referenced categories and members all belong to this household.
            var categoryIds = request.LineItems?.Select(l => l.CategoryId).Distinct().ToList() ?? [];
            var validCategoryIds = await db.Categories
                .Where(c => c.HouseholdId == householdId && categoryIds.Contains(c.Id))
                .Select(c => c.Id).ToListAsync(ct);
            var memberIds = request.LineItems?.Where(l => l.ForMemberId is not null)
                .Select(l => l.ForMemberId!.Value).Distinct().ToList() ?? [];
            var validMemberIds = await db.Members
                .Where(m => m.HouseholdId == householdId && memberIds.Contains(m.Id))
                .Select(m => m.Id).ToListAsync(ct);

            for (var i = 0; request.LineItems is not null && i < request.LineItems.Count; i++)
            {
                var line = request.LineItems[i];
                if (string.IsNullOrWhiteSpace(line.Description))
                {
                    errors[$"lineItems[{i}].description"] = ["Description is required."];
                }
                if (!validCategoryIds.Contains(line.CategoryId))
                {
                    errors[$"lineItems[{i}].categoryId"] = ["Choose a category for this line."];
                }
                if (line.ForMemberId is Guid forId && !validMemberIds.Contains(forId))
                {
                    errors[$"lineItems[{i}].forMemberId"] = ["That member is not in your household."];
                }
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var merchant = await GetOrCreateMerchant(db, householdId, request.Merchant.Trim(), ct);

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
                Source = ExpenseSource.Manual,
            };

            decimal total = 0;
            foreach (var line in request.LineItems!)
            {
                var quantity = line.Quantity is > 0 ? line.Quantity.Value : 1m;
                var unitPrice = line.UnitPrice ?? 0m;
                var amount = line.Amount ?? decimal.Round(quantity * unitPrice, 2);
                total += amount;

                var resolved = await catalog.ResolveAsync(
                    householdId, merchant.Id, line.Description.Trim(), line.CategoryId, line.ShortForm, ct);
                var valueTag = await GetOrCreateValueTag(db, householdId, user.GetMemberId(), line.ValueTag, ct);

                expense.LineItems.Add(new LineItem
                {
                    Id = Guid.NewGuid(),
                    ExpenseId = expense.Id,
                    Description = line.Description.Trim(),
                    ItemId = resolved.Item.Id,
                    CategoryId = line.CategoryId,
                    ForMemberId = line.ForMemberId,
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                    Amount = amount,
                    ValueTagId = valueTag?.Id,
                    Notes = Trimmed(line.Notes),
                });
            }

            expense.Total = total;
            db.Expenses.Add(expense);
            await db.SaveChangesAsync(ct);

            var responses = await ToResponses(db, householdId, [expense], ct);
            return Results.Created($"/expenses/{expense.Id}", responses[0]);
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

    private static async Task<ValueTag?> GetOrCreateValueTag(ExpensesDbContext db, Guid householdId, Guid memberId, string? name, CancellationToken ct)
    {
        name = Trimmed(name);
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

    // Builds responses with the human-readable names the web app shows, looked up in bulk.
    private static async Task<List<ExpenseResponse>> ToResponses(ExpensesDbContext db, Guid householdId, List<Expense> expenses, CancellationToken ct)
    {
        var merchants = await db.Merchants.AsNoTracking().Where(m => m.HouseholdId == householdId).ToDictionaryAsync(m => m.Id, m => m.Name, ct);
        var methods = await db.PaymentMethods.AsNoTracking().Where(p => p.HouseholdId == householdId).ToDictionaryAsync(p => p.Id, p => p.Label, ct);
        var categories = await db.Categories.AsNoTracking().Where(c => c.HouseholdId == householdId).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var members = await db.Members.AsNoTracking().Where(m => m.HouseholdId == householdId).ToDictionaryAsync(m => m.Id, m => m.DisplayName, ct);
        var tags = await db.ValueTags.AsNoTracking().Where(t => t.HouseholdId == householdId).ToDictionaryAsync(t => t.Id, t => t.Name, ct);

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
                l.ValueTagId is Guid vt ? Name(tags, vt) : null,
                l.Notes,
                l.ItemId,
                l.ItemId is Guid it
                    ? shortForms.FirstOrDefault(d => d.ItemId == it && d.MerchantId == e.MerchantId)?.PrintedDescription
                    : null))
                .ToList()))
            .ToList();
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
