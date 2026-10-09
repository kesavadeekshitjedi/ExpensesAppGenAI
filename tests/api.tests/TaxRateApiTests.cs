using System.Net;
using System.Net.Http.Json;
using Expenses.Api.TaxRates;

namespace Expenses.Api.Tests;

public class TaxRateApiTests
{
    private record RateDto(string location, decimal combinedRate, decimal stateRate, decimal localRate);

    [Fact]
    public void Parse_ReadsCombinedStateAndLocalRates_FromDorXml()
    {
        const string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<response loccode=\"1724\" localrate=\".039\" rate=\".104\" code=\"2\" xmlns=\"\">" +
            "<addressline code=\"1724\" street=\"NE 85TH ST\" zip=\"98052\" />" +
            "<rate name=\"REDMOND\" code=\"1724\" staterate=\".065\" localrate=\".039\" /></response>";

        var rate = WaDorSalesTaxRateLookup.Parse(xml);

        Assert.NotNull(rate);
        Assert.Equal("REDMOND", rate!.Location);
        Assert.Equal(0.104m, rate.CombinedRate);
        Assert.Equal(0.065m, rate.StateRate);
        Assert.Equal(0.039m, rate.LocalRate);
    }

    [Fact]
    public void Parse_ReturnsNull_OnErrorCode()
    {
        const string xml = "<response rate=\"\" code=\"5\" xmlns=\"\"></response>";
        Assert.Null(WaDorSalesTaxRateLookup.Parse(xml));
    }

    [Fact]
    public async Task GetTaxRate_RequiresZip()
    {
        var factory = new TestApiFactory();
        await factory.SeedBaselineAsync();
        await using var _f = factory;
        var client = factory.CreateClient();

        var res = await client.GetAsync("/tax-rate");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task GetTaxRate_ReturnsLookupResult()
    {
        var factory = new TestApiFactory();
        await factory.SeedBaselineAsync();
        await using var _f = factory;
        factory.TaxRates.Result = new SalesTaxRate("REDMOND", 0.104m, 0.065m, 0.039m);
        var client = factory.CreateClient();

        var rate = await client.GetFromJsonAsync<RateDto>("/tax-rate?zip=98052");
        Assert.Equal("REDMOND", rate!.location);
        Assert.Equal(0.104m, rate.combinedRate);
    }

    [Fact]
    public async Task GetTaxRate_NotFound_WhenNoRate()
    {
        var factory = new TestApiFactory();
        await factory.SeedBaselineAsync();
        await using var _f = factory;
        factory.TaxRates.Result = null;
        var client = factory.CreateClient();

        var res = await client.GetAsync("/tax-rate?zip=00000");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
