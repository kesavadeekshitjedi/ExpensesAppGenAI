using System.Globalization;
using System.Text.RegularExpressions;

namespace Expenses.Api.Invoices;

// Parses the text of an Amazon "Order Summary" print page (saved to PDF). That format is highly
// consistent: an order number (123-1234567-1234567), an "Order placed <date>" line, an Order Summary
// box (Item(s) Subtotal / Shipping / Estimated tax / Grand Total), and one or more shipment groups of
// items, each item being a (possibly wrapped) title followed by "Sold by:" and a "$" price, with an
// optional quantity badge. Order-level fields (order number, date, grand total, tax) are extracted
// reliably; line items are best-effort and reviewed by the user before saving.
public sealed partial class AmazonInvoiceParser : IInvoiceParser
{
    public ParsedInvoice? TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var orderNumber = OrderNumberRegex().Match(text) is { Success: true } o ? o.Groups[1].Value : null;
        var looksAmazon = orderNumber is not null || text.Contains("amazon", StringComparison.OrdinalIgnoreCase);
        if (!looksAmazon)
        {
            return null;
        }

        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        // Grand total prefers the labeled value; if the amounts box renders labels apart from their
        // values, fall back to the largest currency amount on the page (the grand total is the sum, so
        // it is always >= any single line or subtotal).
        var grandTotal = Money(GrandTotalRegex().Match(text)) ?? MaxCurrency(text);

        return new ParsedInvoice(
            Vendor: "Amazon",
            OrderNumber: orderNumber,
            OrderDate: ParseOrderDate(text),
            Subtotal: Money(SubtotalRegex().Match(text)),
            Tax: Money(TaxRegex().Match(text)),
            GrandTotal: grandTotal,
            Lines: ParseLines(lines));
    }

    private static decimal? MaxCurrency(string text)
    {
        decimal? max = null;
        foreach (Match m in CurrencyRegex().Matches(text))
        {
            if (decimal.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
                && (max is null || v > max))
            {
                max = v;
            }
        }
        return max;
    }

    private static DateOnly? ParseOrderDate(string text)
    {
        var m = OrderPlacedRegex().Match(text);
        if (!m.Success)
        {
            return null;
        }
        var raw = m.Groups[1].Value.Replace(".", "").Trim();
        return DateTime.TryParse(raw, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var dt)
            ? DateOnly.FromDateTime(dt)
            : null;
    }

    private static decimal? Money(Match m) =>
        m.Success && decimal.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;

    // Items are a single-column list, so reading order is reliable: each item's title lines sit just
    // above a "Sold by:" marker, and its price is the next "$x.xx" after that marker. A bare 1-2 digit
    // line within the block is the quantity badge.
    private static List<ParsedInvoiceLine> ParseLines(List<string> lines)
    {
        var result = new List<ParsedInvoiceLine>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (!SoldByRegex().IsMatch(lines[i]))
            {
                continue;
            }

            // Title: contiguous non-noise lines immediately above "Sold by:".
            var titleStart = i;
            while (titleStart - 1 >= 0 && !IsNoise(lines[titleStart - 1]))
            {
                titleStart--;
            }
            if (titleStart == i)
            {
                continue; // no title found above the marker
            }
            // Join the title lines, skipping a bare quantity badge that reads among them.
            var title = string.Join(" ", lines
                .GetRange(titleStart, i - titleStart)
                .Where(l => !QuantityBadgeRegex().IsMatch(l)));

            // Price: the first "$x.xx" after the marker, before the next item's "Sold by:".
            decimal? price = null;
            var priceIndex = lines.Count;
            for (var p = i + 1; p < lines.Count && !SoldByRegex().IsMatch(lines[p]); p++)
            {
                var m = PriceLineRegex().Match(lines[p]);
                if (m.Success)
                {
                    price = decimal.Parse(m.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture);
                    priceIndex = p;
                    break;
                }
            }
            if (price is null)
            {
                continue;
            }

            // Quantity badge: a bare 1-2 digit line within the block (title area through the price).
            var quantity = 1m;
            for (var q = titleStart; q < priceIndex; q++)
            {
                if (q != i && QuantityBadgeRegex().IsMatch(lines[q]))
                {
                    quantity = int.Parse(lines[q], CultureInfo.InvariantCulture);
                    break;
                }
            }

            result.Add(new ParsedInvoiceLine(title, quantity, price, decimal.Round(quantity * price.Value, 2)));
        }
        return result;
    }

    // Lines that can never be part of an item title (section headers, the amounts box, boilerplate).
    private static bool IsNoise(string line) => NoiseRegex().IsMatch(line) || PriceLineRegex().IsMatch(line);

    [GeneratedRegex(@"\b(\d{3}-\d{7}-\d{7})\b")]
    private static partial Regex OrderNumberRegex();

    [GeneratedRegex(@"Order placed\s+([A-Za-z]+\.?\s+\d{1,2},?\s+\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex OrderPlacedRegex();

    [GeneratedRegex(@"Grand Total[:\s]*\$?\s*([\d,]+\.\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex GrandTotalRegex();

    [GeneratedRegex(@"Item\(s\) Subtotal[:\s]*\$?\s*([\d,]+\.\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex SubtotalRegex();

    [GeneratedRegex(@"Estimated tax[\s\S]{0,40}?\$\s*([\d,]+\.\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex TaxRegex();

    [GeneratedRegex(@"^Sold by:", RegexOptions.IgnoreCase)]
    private static partial Regex SoldByRegex();

    [GeneratedRegex(@"^-?\$\s*([\d,]+\.\d{2})$")]
    private static partial Regex PriceLineRegex();

    [GeneratedRegex(@"\$\s*([\d,]+\.\d{2})")]
    private static partial Regex CurrencyRegex();

    [GeneratedRegex(@"^\d{1,2}$")]
    private static partial Regex QuantityBadgeRegex();

    [GeneratedRegex(@"^(Sold by|Supplied by|Return|Your package|Delivered|Arriving|Shipped|Preparing|View related|Back to top|Order Summary|Order placed|Order #|Ship to|Payment method|Item\(s\)|Shipping|Free Shipping|Total before tax|Estimated tax|collected|Grand Total|Conditions of Use|Privacy|Consumer Health|Your Ads|United States|\d{5}(-\d{4})?$)", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseRegex();
}
