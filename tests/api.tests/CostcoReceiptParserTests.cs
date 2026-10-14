using Expenses.Api.Invoices;

namespace Expenses.Api.Tests;

// Unit tests for the Costco receipt parser, using text that mirrors the real sample gas receipts.
public class CostcoReceiptParserTests
{
    private readonly CostcoReceiptParser _parser = new();

    private const string GasReceipt = """
        Redmond #1225
        7725 188TH AVE NE
        REDMOND, WA 98052
        Member 112053396512
        Invoice# 96308
        Date: 10/07/26
        Time: 15:18
        Auth# 75488D
        VI Acct #
        ************7039
        Pump Gallons Price
        18 15.868 $5.799
        Product Amount
        Premium $92.02
        Total Sale $92.02
        Sale - Contactless
        Approved
        TranID# 628018096308
        Thank you for your purchase of Kirkland
        Signature Fuel.
        Visit Costco.com
        Search: Fuel
        """;

    [Fact]
    public void ParsesGasReceiptOrderLevelFields()
    {
        var parsed = _parser.TryParse(GasReceipt);

        Assert.NotNull(parsed);
        Assert.Equal("Costco", parsed!.Vendor);
        Assert.Equal("628018096308", parsed.OrderNumber); // TranID is the dedup key
        Assert.Equal(new DateOnly(2026, 10, 7), parsed.OrderDate);
        Assert.Equal(92.02m, parsed.GrandTotal);
        Assert.Null(parsed.Tax);
    }

    [Fact]
    public void ParsesGasLineWithGallonsAndPricePerGallon()
    {
        var parsed = _parser.TryParse(GasReceipt)!;

        var line = Assert.Single(parsed.Lines);
        Assert.Equal("Costco Premium Fuel", line.Description);
        Assert.Equal(15.868m, line.Quantity);
        Assert.Equal(5.799m, line.UnitPrice);
        Assert.Equal(92.02m, line.Amount);
    }

    [Fact]
    public void ReturnsNullForNonCostcoText()
    {
        Assert.Null(_parser.TryParse("Some store\nTotal Sale $5.00")); // has a total but no Costco marker
        Assert.Null(_parser.TryParse("Visit Costco.com\nno total here"));
    }
}
