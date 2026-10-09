using System.Net.Http.Json;

namespace Expenses.Api.Tests;

// Covers the time-series report endpoints that power the spend-over-time and item price-trend charts.
public class ReportTrendApiTests
{
    private record LinePayload(string description, Guid categoryId, decimal? unitPrice, decimal? amount);
    private record ExpensePayload(string merchant, Guid paymentMethodId, string? date, decimal? tax, string? notes, object[] lineItems);
    private record LineItemDto(Guid itemId, decimal unitPrice);
    private record ExpenseDto(List<LineItemDto> lineItems);
    private record TrendPointDto(string period, decimal total, int count);
    private record TrendDto(string interval, List<TrendPointDto> points);
    private record PricePointDto(string date, decimal unitPrice, string merchant);
    private record HistoryDto(Guid itemId, string fullName, List<PricePointDto> points);

    private static async Task<(TestApiFactory factory, HttpClient client, Guid itemId)> SeedPurchaseAsync()
    {
        var factory = new TestApiFactory();
        var (category, method) = await factory.SeedBaselineAsync();
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/expenses", new ExpensePayload("Costco", method.Id, null, null, null,
            [new LinePayload("Kirkland Organic Eggs", category.Id, 3.50m, null)]));
        res.EnsureSuccessStatusCode();
        var expense = await res.Content.ReadFromJsonAsync<ExpenseDto>();
        return (factory, client, expense!.lineItems[0].itemId);
    }

    [Fact]
    public async Task Trend_IncludesSpendInRange()
    {
        var (factory, client, _) = await SeedPurchaseAsync();
        await using var _f = factory;

        var trend = await client.GetFromJsonAsync<TrendDto>("/reports/trend?interval=month");
        Assert.NotNull(trend);
        Assert.Equal("month", trend!.interval);
        Assert.Equal(3.50m, trend.points.Sum(p => p.total));
    }

    [Fact]
    public async Task ItemPriceHistory_ReturnsPurchasePoints()
    {
        var (factory, client, itemId) = await SeedPurchaseAsync();
        await using var _f = factory;

        var history = await client.GetFromJsonAsync<HistoryDto>($"/reports/item-price-history?itemId={itemId}");
        Assert.NotNull(history);
        var point = Assert.Single(history!.points);
        Assert.Equal(3.50m, point.unitPrice);
        Assert.Equal("Costco", point.merchant);
    }
}
