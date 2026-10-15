using System.Net.Http.Json;

namespace Expenses.Api.Tests;

// Covers the settings behaviors added for the import UX: categories infer their taxability from the name
// (so one added on the fly during import is sensible), and a household can pick a default payment method
// (at most one), which the entry/import forms pre-select.
public class SettingsApiTests
{
    private record CategoryDto(Guid id, string name, bool archived, bool isTaxable);
    private record PaymentMethodDto(Guid id, string label, string type, bool archived, bool isDefault);

    [Fact]
    public async Task AddCategory_InfersTaxabilityFromName_UnlessGiven()
    {
        await using var factory = new TestApiFactory();
        await factory.SeedBaselineAsync();
        var client = factory.CreateClient();

        var veg = await (await client.PostAsJsonAsync("/categories", new { name = "Vegetables" })).Content.ReadFromJsonAsync<CategoryDto>();
        var electronics = await (await client.PostAsJsonAsync("/categories", new { name = "Electronics" })).Content.ReadFromJsonAsync<CategoryDto>();
        var forced = await (await client.PostAsJsonAsync("/categories", new { name = "Fresh produce", isTaxable = true })).Content.ReadFromJsonAsync<CategoryDto>();

        Assert.False(veg!.isTaxable);        // a food/grocery name -> non-taxable (WA)
        Assert.True(electronics!.isTaxable); // anything else -> taxable
        Assert.True(forced!.isTaxable);      // an explicit value wins over the guess
    }

    [Fact]
    public async Task PaymentMethods_DefaultIsSingular_AndCanMove()
    {
        await using var factory = new TestApiFactory();
        await factory.SeedBaselineAsync();
        var client = factory.CreateClient();

        var amex = await (await client.PostAsJsonAsync("/payment-methods", new { label = "Amex 1005", type = "CreditCard" })).Content.ReadFromJsonAsync<PaymentMethodDto>();
        var costco = await (await client.PostAsJsonAsync("/payment-methods", new { label = "Costco Citi 7039", type = "CreditCard" })).Content.ReadFromJsonAsync<PaymentMethodDto>();

        await client.PatchAsJsonAsync($"/payment-methods/{costco!.id}", new { isDefault = true });
        var afterFirst = await client.GetFromJsonAsync<List<PaymentMethodDto>>("/payment-methods");
        Assert.Equal(costco.id, afterFirst!.Single(p => p.isDefault).id);

        // Moving the default to another method clears it from the first (at most one default).
        await client.PatchAsJsonAsync($"/payment-methods/{amex!.id}", new { isDefault = true });
        var afterMove = await client.GetFromJsonAsync<List<PaymentMethodDto>>("/payment-methods");
        Assert.Equal(amex.id, afterMove!.Single(p => p.isDefault).id);
    }
}
