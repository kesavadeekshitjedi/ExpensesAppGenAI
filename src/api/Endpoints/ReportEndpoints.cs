using System.Security.Claims;
using Expenses.Api.Auth;
using Expenses.Api.Data;
using Expenses.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Endpoints;

public static class ReportEndpoints
{
    // One row of a breakdown: a label, its total, and how many line items make it up.
    public record Bucket(string Key, decimal Total, int Count);

    public record ReportSummary(
        DateOnly From,
        DateOnly To,
        decimal Total,
        int LineItemCount,
        List<Bucket> ByCategory,
        List<Bucket> ByFor,
        List<Bucket> ByPaymentMethod,
        List<Bucket> ByMerchant,
        List<Bucket> ByItem,
        List<Bucket> ByValueTag);

    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        // Reports are household-wide and readable by everyone, including view-only children (SPEC feature 11).
        var group = app.MapGroup("/reports").RequireAuthorization();

        // Spending over a period, broken down by category, who it was "for", payment method, merchant,
        // item, and value tag. Defaults to the current calendar month when no range is given.
        group.MapGet("/summary", async (
            DateOnly? from,
            DateOnly? to,
            ClaimsPrincipal user,
            ExpensesDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().LocalDateTime);
            var start = from ?? new DateOnly(today.Year, today.Month, 1);
            var end = to ?? today;
            if (end < start)
            {
                (start, end) = (end, start);
            }

            var householdId = user.GetHouseholdId();

            // Load the expenses (with their lines) in range, then group in memory. Household volumes
            // in phase 1 are small, and this keeps the grouping simple and provider-independent.
            var expenses = await db.Expenses.AsNoTracking()
                .Where(e => e.HouseholdId == householdId && e.Date >= start && e.Date <= end)
                .Include(e => e.LineItems)
                .ToListAsync(ct);

            var categories = await NameMap(db.Categories.Where(c => c.HouseholdId == householdId), c => c.Id, c => c.Name, ct);
            var methods = await NameMap(db.PaymentMethods.Where(p => p.HouseholdId == householdId), p => p.Id, p => p.Label, ct);
            var merchants = await NameMap(db.Merchants.Where(m => m.HouseholdId == householdId), m => m.Id, m => m.Name, ct);
            var members = await NameMap(db.Members.Where(m => m.HouseholdId == householdId), m => m.Id, m => m.DisplayName, ct);
            var items = await NameMap(db.Items.Where(i => i.HouseholdId == householdId), i => i.Id, i => i.FullName, ct);
            var tags = await NameMap(db.ValueTags.Where(t => t.HouseholdId == householdId), t => t.Id, t => t.Name, ct);

            // Flatten to (line, owning expense) so a line can see its expense's merchant / payment method.
            var lines = expenses.SelectMany(e => e.LineItems.Select(l => (line: l, expense: e))).ToList();

            string Name(Dictionary<Guid, string> map, Guid id) => map.GetValueOrDefault(id, "(unknown)");

            var summary = new ReportSummary(
                start,
                end,
                lines.Sum(x => x.line.Amount),
                lines.Count,
                Group(lines, x => Name(categories, x.line.CategoryId)),
                Group(lines, x => x.line.ForMemberId is Guid fm ? Name(members, fm) : "Family"),
                Group(lines, x => Name(methods, x.expense.PaymentMethodId)),
                Group(lines, x => Name(merchants, x.expense.MerchantId)),
                Group(lines, x => x.line.ItemId is Guid it ? Name(items, it) : x.line.Description),
                Group(lines.Where(x => x.line.ValueTagId is not null), x => Name(tags, x.line.ValueTagId!.Value)));

            return Results.Ok(summary);
        });
    }

    private static List<Bucket> Group(
        IEnumerable<(LineItem line, Expense expense)> lines,
        Func<(LineItem line, Expense expense), string> key) =>
        lines
            .GroupBy(key)
            .Select(g => new Bucket(g.Key, g.Sum(x => x.line.Amount), g.Count()))
            .OrderByDescending(b => b.Total)
            .ToList();

    private static async Task<Dictionary<Guid, string>> NameMap<T>(
        IQueryable<T> query, Func<T, Guid> id, Func<T, string> name, CancellationToken ct) where T : class =>
        (await query.AsNoTracking().ToListAsync(ct)).ToDictionary(id, name);
}
