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
}
