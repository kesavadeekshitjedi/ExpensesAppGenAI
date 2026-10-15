using System.Net;
using System.Net.Http.Json;

namespace Expenses.Api.Tests;

// Covers the "last price" lookup that powers the previous-price / % change banner during entry.
public class ItemPriceApiTests
{
    private record LinePayload(string description, Guid categoryId, decimal? unitPrice, decimal? amount);
    private record ExpensePayload(string merchant, Guid paymentMethodId, string? date, decimal? tax, string? notes, object[] lineItems);
    private record LastPriceDto(string fullName, decimal unitPrice, decimal amount, decimal quantity, string date, string merchant);

    [Fact]
    public async Task LastPrice_ReturnsMostRecentPurchase()
    {
        var factory = new TestApiFactory();
        var (category, method) = await factory.SeedBaselineAsync();
        await using var _f = factory;
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/expenses", new ExpensePayload("Costco", method.Id, null, null, null,
            [new LinePayload("Kirkland Organic Eggs", category.Id, 3.50m, null)]));

        var lp = await client.GetFromJsonAsync<LastPriceDto>("/items/last-price?name=Kirkland Organic Eggs");
        Assert.NotNull(lp);
        Assert.Equal(3.50m, lp!.unitPrice);
        Assert.Equal("Costco", lp.merchant);
    }

    [Fact]
    public async Task LastPrice_UnknownItem_ReturnsNoContent()
    {
        var factory = new TestApiFactory();
        await factory.SeedBaselineAsync();
        await using var _f = factory;
        var client = factory.CreateClient();

        var res = await client.GetAsync("/items/last-price?name=Nonexistent");
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    private record PricePointDto(string date, decimal unitPrice, decimal amount, decimal quantity, string merchant);
    private record PriceHistoryDto(Guid itemId, string fullName, List<PricePointDto> points);

    // Gas gets itemized into separate items (Premium/Regular fuel), so the per-item trend is sparse.
    // The category trend aggregates them, which is what the user actually wants to see.
    [Fact]
    public async Task PriceHistory_ByCategory_AggregatesAllItemsInIt()
    {
        var factory = new TestApiFactory();
        var (_, method) = await factory.SeedBaselineAsync();
        await using var _f = factory;
        var client = factory.CreateClient();

        var gas = await (await client.PostAsJsonAsync("/categories", new { name = "Gas", isTaxable = false })).Content.ReadFromJsonAsync<CategoryDto>();

        // Two fill-ups on different dates, recorded as different items (premium vs regular).
        await client.PostAsJsonAsync("/expenses", new ExpensePayload("Costco Gas", method.Id, "2026-10-01", null, null,
            [new LinePayload("Costco Premium Fuel", gas!.id, 5.80m, 92.02m)]));
        await client.PostAsJsonAsync("/expenses", new ExpensePayload("Costco Gas", method.Id, "2026-10-08", null, null,
            [new LinePayload("Costco Regular Fuel", gas.id, 5.20m, 75.00m)]));

        // Per-item history is sparse (one point each); the category history has both.
        var byCategory = await client.GetFromJsonAsync<PriceHistoryDto>($"/reports/item-price-history?categoryId={gas.id}");
        Assert.Equal("Gas", byCategory!.fullName);
        Assert.Equal(2, byCategory.points.Count);
    }

    private record CategoryDto(Guid id, string name);
}
