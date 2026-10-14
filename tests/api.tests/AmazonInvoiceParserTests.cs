using Expenses.Api.Invoices;

namespace Expenses.Api.Tests;

// Unit tests for the Amazon "Order Summary" text parser, using text that mirrors the real sample PDFs
// (invoice1/inv4: two items, one with a quantity badge; inv3: a multi-shipment order with free shipping).
public class AmazonInvoiceParserTests
{
    private readonly AmazonInvoiceParser _parser = new();

    private const string Invoice1 = """
        Order Summary
        Order placed October 12, 2026   Order # 112-5242793-5269063
        Ship to
        Kesava Addepalli
        24615 NE 13TH PL
        SAMMAMISH, WA 98074-5068
        United States
        Payment method
        Prime Visa 3352
        Order Summary
        Item(s) Subtotal: $21.97
        Shipping & Handling: $1.50
        Total before tax: $23.47
        Estimated tax to be
        collected:
        $0.87
        Grand Total: $24.34
        Delivered October 13
        Your package was left near the front door or porch.
        LEGO Creator 3 in 1 Space Shuttle Toys - Space Toys Building Set for Boys & Girls,
        Ages 6+ - Option to Build 2 Spaceships or Astronaut - Educational Gift for Kids
        Birthday & Holidays - 31134
        Sold by: Amazon.com
        Supplied by: Other
        Return or replace items: Eligible through November 12, 2026
        $7.99
        2
        Kraft Gluten Free Mac and Cheese, 4 Microwaveable Cups, Original, 1.9 oz
        Sold by: Amazon.com
        Supplied by: Other
        Return items: Eligible through November 12, 2026
        $6.99
        """;

    [Fact]
    public void ParsesOrderLevelFields()
    {
        var parsed = _parser.TryParse(Invoice1);

        Assert.NotNull(parsed);
        Assert.Equal("Amazon", parsed!.Vendor);
        Assert.Equal("112-5242793-5269063", parsed.OrderNumber);
        Assert.Equal(new DateOnly(2026, 10, 12), parsed.OrderDate);
        Assert.Equal(21.97m, parsed.Subtotal);
        Assert.Equal(0.87m, parsed.Tax);
        Assert.Equal(24.34m, parsed.GrandTotal);
    }

    [Fact]
    public void ParsesLinesIncludingQuantityBadge()
    {
        var parsed = _parser.TryParse(Invoice1)!;

        Assert.Equal(2, parsed.Lines.Count);

        var lego = parsed.Lines[0];
        Assert.StartsWith("LEGO Creator 3 in 1 Space Shuttle", lego.Description);
        Assert.Equal(1m, lego.Quantity);
        Assert.Equal(7.99m, lego.Amount);

        var kraft = parsed.Lines[1];
        Assert.Equal("Kraft Gluten Free Mac and Cheese, 4 Microwaveable Cups, Original, 1.9 oz", kraft.Description);
        Assert.Equal(2m, kraft.Quantity);
        Assert.Equal(6.99m, kraft.UnitPrice);
        Assert.Equal(13.98m, kraft.Amount);

        // Line amounts reconcile to the item subtotal.
        Assert.Equal(parsed.Subtotal, parsed.Lines.Sum(l => l.Amount));
    }

    [Fact]
    public void ParsesMultiShipmentOrderWithFreeShipping()
    {
        const string inv3 = """
            Order placed September 27, 2026   Order # 112-8041743-7005038
            Order Summary
            Item(s) Subtotal: $11.47
            Shipping & Handling: $2.24
            Free Shipping:
            -$2.24
            Total before tax: $11.47
            Estimated tax to be
            collected:
            $0.00
            Grand Total: $11.47
            Delivered September 28
            Your package was left near the front door or porch.
            Green Seedless Grapes, 3 Lb
            Sold by: Amazon.com
            Supplied by: Other
            Return items: Eligible through October 28, 2026
            $5.49
            Wonderful Halos Mandarins, Sweet, Seedless, Easy Peel Mandarin Oranges -
            Healthy Food, Fresh Fruit, 3lb Bag
            Sold by: Amazon.com
            Supplied by: Other
            Return items: Eligible through October 28, 2026
            $4.99
            Delivered September 28
            Your package was left near the front door or porch.
            Banana Bunch (4-5 Count)
            Sold by: Amazon.com
            Supplied by: Other
            Return items: Eligible through October 28, 2026
            $0.99
            """;

        var parsed = _parser.TryParse(inv3)!;

        Assert.Equal("112-8041743-7005038", parsed.OrderNumber);
        Assert.Equal(11.47m, parsed.GrandTotal);
        Assert.Equal(3, parsed.Lines.Count);
        Assert.Equal(5.49m, parsed.Lines[0].Amount);
        Assert.Equal("Wonderful Halos Mandarins, Sweet, Seedless, Easy Peel Mandarin Oranges - Healthy Food, Fresh Fruit, 3lb Bag", parsed.Lines[1].Description);
        Assert.Equal(0.99m, parsed.Lines[2].Amount);
        Assert.Equal(parsed.Subtotal, parsed.Lines.Sum(l => l.Amount));
    }

    [Fact]
    public void ReturnsNullForNonAmazonText()
    {
        Assert.Null(_parser.TryParse("Some random receipt from a local store\nTotal: $5.00"));
    }
}
