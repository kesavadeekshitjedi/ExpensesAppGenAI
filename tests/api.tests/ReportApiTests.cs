using System.Net.Http.Json;
using Expenses.Api.Domain;

namespace Expenses.Api.Tests;

public class ReportApiTests
{
    private record LinePayload(string description, Guid categoryId, Guid? forMemberId, decimal? quantity,
        decimal? unitPrice, decimal? amount, string? valueTag, string? notes, string? shortForm);
    private record ExpensePayload(string merchant, Guid paymentMethodId, string? date, decimal? tax,
        string? notes, object[] lineItems);

    private record Bucket(string key, decimal total, int count);
    private record Summary(string from, string to, decimal total, int lineItemCount,
        List<Bucket> byCategory, List<Bucket> byFor, List<Bucket> byMerchant, List<Bucket> byValueTag);

    [Fact]
    public async Task Summary_TotalsAndBreaksDownSpendingInRange()
    {
        await using var factory = new TestApiFactory();
        var (category, method) = await factory.SeedBaselineAsync();
        var client = factory.CreateClient();

        // Two expenses inside the range...
        await client.PostAsJsonAsync("/expenses", new ExpensePayload("Costco", method.Id, "2026-10-03", null, null,
            [new LinePayload("Eggs", category.Id, null, 1, 5m, null, "Needed", null, null)]));
        await client.PostAsJsonAsync("/expenses", new ExpensePayload("Target", method.Id, "2026-10-04", null, null,
            [new LinePayload("Toy", category.Id, factory.ParentMemberId, 1, 20m, null, "Splurge", null, null)]));
        // ...and one outside it, which must be excluded.
        await client.PostAsJsonAsync("/expenses", new ExpensePayload("Costco", method.Id, "2026-09-01", null, null,
            [new LinePayload("Old", category.Id, null, 1, 99m, null, null, null, null)]));

        var summary = await client.GetFromJsonAsync<Summary>("/reports/summary?from=2026-10-01&to=2026-10-31");

        Assert.NotNull(summary);
        Assert.Equal(25m, summary!.total);                 // 5 + 20, the September one excluded
        Assert.Equal(2, summary.lineItemCount);

        // "For": one line is Family (null), one is the parent.
        Assert.Contains(summary.byFor, b => b.key == "Family" && b.total == 5m);
        Assert.Contains(summary.byFor, b => b.key == "Parent" && b.total == 20m);

        // Merchants sorted by total descending.
        Assert.Equal("Target", summary.byMerchant[0].key);

        // Value tags only cover tagged lines.
        Assert.Equal(2, summary.byValueTag.Count);
    }

    [Fact]
    public async Task Summary_DefaultsToTheCurrentMonth_AndIsReadableByChildren()
    {
        await using var factory = new TestApiFactory { Role = MemberRole.Child };
        await factory.SeedBaselineAsync();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/reports/summary");

        response.EnsureSuccessStatusCode();   // children can view reports (SPEC feature 11)
    }
}
