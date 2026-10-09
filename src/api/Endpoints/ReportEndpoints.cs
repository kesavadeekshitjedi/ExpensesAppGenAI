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
        List<Bucket> ByValueTag,
        List<Bucket> ByVehicle);

    // A point on a spending-over-time chart.
    public record TrendPoint(string Period, decimal Total, int Count);
    public record TrendResponse(DateOnly From, DateOnly To, string Interval, List<TrendPoint> Points);

    // A point on an item's price-over-time chart.
    public record PricePoint(string Date, decimal UnitPrice, decimal Amount, decimal Quantity, string Merchant);
    public record ItemPriceHistoryResponse(Guid ItemId, string FullName, List<PricePoint> Points);

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
            var vehicles = await NameMap(db.Vehicles.Where(v => v.HouseholdId == householdId), v => v.Id, v => v.Name, ct);

            // Flatten to (line, owning expense) so a line can see its expense's merchant / payment method.
            var lines = expenses.SelectMany(e => e.LineItems.Select(l => (line: l, expense: e))).ToList();

            string Name(Dictionary<Guid, string> map, Guid id) => map.GetValueOrDefault(id, "(unknown)");

            var summary = new ReportSummary(
                start,
                end,
                lines.Sum(x => x.line.Amount + x.line.AllocatedTax),
                lines.Count,
                Group(lines, x => Name(categories, x.line.CategoryId)),
                Group(lines, x => x.line.ForMemberId is Guid fm ? Name(members, fm) : "Family"),
                Group(lines, x => Name(methods, x.expense.PaymentMethodId)),
                Group(lines, x => Name(merchants, x.expense.MerchantId)),
                Group(lines, x => x.line.ItemId is Guid it ? Name(items, it) : x.line.Description),
                Group(lines.Where(x => x.line.ValueTagId is not null), x => Name(tags, x.line.ValueTagId!.Value)),
                Group(lines.Where(x => x.line.VehicleId is not null), x => Name(vehicles, x.line.VehicleId!.Value)));

            return Results.Ok(summary);
        });

        // Spending over time, bucketed by day / week / month, with empty periods filled so the chart is
        // continuous. Defaults to the last 6 months by month.
        group.MapGet("/trend", async (
            DateOnly? from, DateOnly? to, string? interval,
            ClaimsPrincipal user, ExpensesDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().LocalDateTime);
            var unit = (interval ?? "month").ToLowerInvariant();
            var end = to ?? today;
            var start = from ?? unit switch
            {
                "day" => end.AddDays(-29),
                "week" => end.AddDays(-7 * 11),
                _ => end.AddMonths(-5),
            };
            if (end < start)
            {
                (start, end) = (end, start);
            }

            var householdId = user.GetHouseholdId();
            var expenses = await db.Expenses.AsNoTracking()
                .Where(e => e.HouseholdId == householdId && e.Date >= start && e.Date <= end)
                .Include(e => e.LineItems)
                .ToListAsync(ct);

            var totals = new Dictionary<string, (decimal total, int count)>();
            foreach (var e in expenses)
            {
                var key = BucketKey(e.Date, unit);
                var sum = e.LineItems.Sum(l => l.Amount + l.AllocatedTax);
                var prev = totals.GetValueOrDefault(key);
                totals[key] = (prev.total + sum, prev.count + e.LineItems.Count);
            }

            var points = PeriodKeys(start, end, unit)
                .Select(k =>
                {
                    var v = totals.GetValueOrDefault(k);
                    return new TrendPoint(k, v.total, v.count);
                })
                .ToList();

            return Results.Ok(new TrendResponse(start, end, unit, points));
        });

        // Price history for one item over time, for the per-item price-trend chart.
        group.MapGet("/item-price-history", async (
            Guid itemId, DateOnly? from, DateOnly? to,
            ClaimsPrincipal user, ExpensesDbContext db, CancellationToken ct) =>
        {
            var householdId = user.GetHouseholdId();
            var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && i.HouseholdId == householdId, ct);
            if (item is null)
            {
                return Results.NotFound();
            }

            var query = from l in db.LineItems.AsNoTracking()
                        join e in db.Expenses.AsNoTracking() on l.ExpenseId equals e.Id
                        where l.ItemId == itemId && e.HouseholdId == householdId
                        select new { l.UnitPrice, l.Amount, l.Quantity, e.Date, e.MerchantId };
            if (from is DateOnly f) query = query.Where(x => x.Date >= f);
            if (to is DateOnly t) query = query.Where(x => x.Date <= t);

            var rows = await query.OrderBy(x => x.Date).ToListAsync(ct);
            var merchants = await NameMap(db.Merchants.Where(m => m.HouseholdId == householdId), m => m.Id, m => m.Name, ct);

            var points = rows
                .Select(r => new PricePoint(
                    r.Date.ToString("yyyy-MM-dd"), r.UnitPrice, r.Amount, r.Quantity,
                    merchants.GetValueOrDefault(r.MerchantId, "(unknown)")))
                .ToList();

            return Results.Ok(new ItemPriceHistoryResponse(item.Id, item.FullName, points));
        });
    }

    private static DateOnly WeekStart(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7)); // Monday

    private static string BucketKey(DateOnly d, string unit) => unit switch
    {
        "day" => d.ToString("yyyy-MM-dd"),
        "week" => WeekStart(d).ToString("yyyy-MM-dd"),
        _ => d.ToString("yyyy-MM"),
    };

    private static List<string> PeriodKeys(DateOnly start, DateOnly end, string unit)
    {
        var keys = new List<string>();
        if (unit == "day")
        {
            for (var d = start; d <= end; d = d.AddDays(1)) keys.Add(d.ToString("yyyy-MM-dd"));
        }
        else if (unit == "week")
        {
            for (var d = WeekStart(start); d <= end; d = d.AddDays(7)) keys.Add(d.ToString("yyyy-MM-dd"));
        }
        else
        {
            for (var d = new DateOnly(start.Year, start.Month, 1); d <= new DateOnly(end.Year, end.Month, 1); d = d.AddMonths(1))
                keys.Add(d.ToString("yyyy-MM"));
        }
        return keys;
    }

    private static List<Bucket> Group(
        IEnumerable<(LineItem line, Expense expense)> lines,
        Func<(LineItem line, Expense expense), string> key) =>
        lines
            .GroupBy(key)
            .Select(g => new Bucket(g.Key, g.Sum(x => x.line.Amount + x.line.AllocatedTax), g.Count()))
            .OrderByDescending(b => b.Total)
            .ToList();

    private static async Task<Dictionary<Guid, string>> NameMap<T>(
        IQueryable<T> query, Func<T, Guid> id, Func<T, string> name, CancellationToken ct) where T : class =>
        (await query.AsNoTracking().ToListAsync(ct)).ToDictionary(id, name);
}
