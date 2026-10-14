using System.Globalization;
using System.Text.RegularExpressions;

namespace Expenses.Api.Invoices;

// Parses a Costco receipt PDF. The samples are gas receipts, whose text layer is consistent: a store
// header, "Member / Invoice# / Date: / Time: / Auth#", a "Pump Gallons Price" row with the fuel figures,
// a "Product Amount" block ("Premium $92.02"), a "Total Sale", and a unique "TranID#". We extract the
// order identity (TranID, the dedup key — stable across re-downloads) and a single gas line (gallons as
// quantity, price-per-gallon as unit price). A non-gas Costco receipt falls back to one line for the
// total, which the user can split in review.
public sealed partial class CostcoReceiptParser : IInvoiceParser
{
    public ParsedInvoice? TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var hasTotal = TotalSaleRegex().IsMatch(text);
        var looksCostco = text.Contains("costco", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Kirkland Signature", StringComparison.OrdinalIgnoreCase);
        if (!hasTotal || !looksCostco)
        {
            return null;
        }

        var tranId = Group(TranIdRegex(), text);
        var invoice = Group(InvoiceRegex(), text);
        var total = Money(TotalSaleRegex(), text);

        return new ParsedInvoice(
            Vendor: "Costco",
            OrderNumber: tranId ?? invoice,
            OrderDate: ParseDate(text),
            Subtotal: total,
            Tax: null, // Costco fuel price already includes tax; warehouse receipts list tax separately (not in samples).
            GrandTotal: total,
            Lines: ParseLines(text, total));
    }

    private static List<ParsedInvoiceLine> ParseLines(string text, decimal? total)
    {
        // Gas: gallons and a 3-decimal price-per-gallon, e.g. "15.868 $5.799".
        var fuel = FuelRegex().Match(text);
        var product = ProductRegex().Match(text);
        if (fuel.Success)
        {
            var gallons = decimal.Parse(fuel.Groups[1].Value, CultureInfo.InvariantCulture);
            var pricePerGallon = decimal.Parse(fuel.Groups[2].Value, CultureInfo.InvariantCulture);
            var productName = product.Success ? product.Groups[1].Value.Trim() : "Fuel";
            var amount = product.Success
                ? decimal.Parse(product.Groups[2].Value.Replace(",", ""), CultureInfo.InvariantCulture)
                : decimal.Round(gallons * pricePerGallon, 2);
            return [new ParsedInvoiceLine($"Costco {productName} Fuel", gallons, pricePerGallon, amount, SuggestedCategory: "Gas")];
        }

        // Non-gas fallback: a single line for the whole receipt total.
        return total is decimal t ? [new ParsedInvoiceLine("Costco purchase", 1m, t, t)] : [];
    }

    private static DateOnly? ParseDate(string text)
    {
        var raw = Group(DateRegex(), text);
        if (raw is null)
        {
            return null;
        }
        // Costco prints M/d/yy (e.g. 10/07/26); accept 2- or 4-digit years.
        string[] formats = ["M/d/yy", "MM/dd/yy", "M/d/yyyy", "MM/dd/yyyy"];
        return DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? DateOnly.FromDateTime(dt)
            : null;
    }

    private static string? Group(Regex regex, string text) =>
        regex.Match(text) is { Success: true } m ? m.Groups[1].Value : null;

    private static decimal? Money(Regex regex, string text) =>
        regex.Match(text) is { Success: true } m
            && decimal.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;

    [GeneratedRegex(@"Total Sale\s*\$?\s*([\d,]+\.\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex TotalSaleRegex();

    [GeneratedRegex(@"TranID#\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex TranIdRegex();

    [GeneratedRegex(@"Invoice#\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex InvoiceRegex();

    [GeneratedRegex(@"Date:\s*(\d{1,2}/\d{1,2}/\d{2,4})", RegexOptions.IgnoreCase)]
    private static partial Regex DateRegex();

    // Gallons (3-decimal) then price-per-gallon (3-decimal), e.g. "15.868 $5.799".
    [GeneratedRegex(@"(\d+\.\d{3})\s+\$\s*(\d+\.\d{3})")]
    private static partial Regex FuelRegex();

    // The product line in the "Product / Amount" block, e.g. "Premium $92.02" (not "Total Sale").
    [GeneratedRegex(@"Product\s+Amount\s*[\r\n]+\s*([A-Za-z][A-Za-z ]*?)\s+\$\s*([\d,]+\.\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex ProductRegex();
}
