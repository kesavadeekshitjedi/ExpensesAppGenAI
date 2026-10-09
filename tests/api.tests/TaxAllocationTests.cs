using System.Net.Http.Json;

namespace Expenses.Api.Tests;

// Verifies an expense's tax is spread across its taxable lines and that reports reflect the true,
// tax-inclusive cost.
public class TaxAllocationTests
{
    private record LinePayload(string description, Guid categoryId, decimal? amount);
    private record ExpensePayload(string merchant, Guid paymentMethodId, string? date, decimal? tax, string? notes, object[] lineItems);
    private record CategoryDto(Guid id, string name, bool archived, bool isTaxable);
    private record LineItemDto(string description, decimal amount, decimal allocatedTax, Guid categoryId);
    private record ExpenseDto(Guid id, List<LineItemDto> lineItems);
    private record BucketDto(string key, decimal total, int count);
    private record SummaryDto(decimal total, List<BucketDto> byCategory);

    [Fact]
    public async Task Tax_IsAllocatedToTaxableLinesOnly()
    {
        var factory = new TestApiFactory();
        var (taxableCat, method) = await factory.SeedBaselineAsync(); // Groceries is taxable by default
        await using var _f = factory;
        var client = factory.CreateClient();

        var created = await client.PostAsJsonAsync("/categories", new { name = "Gift card", isTaxable = false });
        var nonTaxable = await created.Content.ReadFromJsonAsync<CategoryDto>();

        var res = await client.PostAsJsonAsync("/expenses", new ExpensePayload("Store", method.Id, null, 10m, null,
        [
            new LinePayload("Taxable thing", taxableCat.Id, 100m),
            new LinePayload("Gift", nonTaxable!.id, 100m),
        ]));
        res.EnsureSuccessStatusCode();

        var expense = await res.Content.ReadFromJsonAsync<ExpenseDto>();
        var taxableLine = expense!.lineItems.Single(l => l.categoryId == taxableCat.Id);
        var giftLine = expense.lineItems.Single(l => l.categoryId == nonTaxable.id);
        Assert.Equal(10m, taxableLine.allocatedTax);
        Assert.Equal(0m, giftLine.allocatedTax);
    }

    [Fact]
    public async Task TaxSplitsProportionally_AndSumsExactly()
    {
        var factory = new TestApiFactory();
        var (cat, method) = await factory.SeedBaselineAsync();
        await using var _f = factory;
        var client = factory.CreateClient();

        // 1.00 tax over 10.00 + 20.00: largest-remainder split should be 0.33 and 0.67, summing to 1.00.
        var res = await client.PostAsJsonAsync("/expenses", new ExpensePayload("Store", method.Id, null, 1m, null,
        [
            new LinePayload("A", cat.Id, 10m),
            new LinePayload("B", cat.Id, 20m),
        ]));
        res.EnsureSuccessStatusCode();

        var expense = await res.Content.ReadFromJsonAsync<ExpenseDto>();
        Assert.Equal(1m, expense!.lineItems.Sum(l => l.allocatedTax));
        Assert.Contains(expense.lineItems, l => l.allocatedTax == 0.33m);
        Assert.Contains(expense.lineItems, l => l.allocatedTax == 0.67m);
    }

    [Fact]
    public async Task ReportTotal_IncludesAllocatedTax()
    {
        var factory = new TestApiFactory();
        var (cat, method) = await factory.SeedBaselineAsync();
        await using var _f = factory;
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/expenses", new ExpensePayload("Store", method.Id, null, 7m, null,
            [new LinePayload("Thing", cat.Id, 100m)]));

        var summary = await client.GetFromJsonAsync<SummaryDto>("/reports/summary");
        Assert.Equal(107m, summary!.total);
        Assert.Equal(107m, Assert.Single(summary.byCategory).total);
    }
}
