using System.Globalization;
using System.Xml.Linq;

namespace Expenses.Api.TaxRates;

// Uses the Washington Department of Revenue's free address/rate web service (no key required) to look up
// the combined state + local sales-tax rate. The response looks like:
//   <response loccode="1724" localrate=".039" rate=".104" code="2">
//     <addressline .../>
//     <rate name="REDMOND" code="1724" staterate=".065" localrate=".039" />
//   </response>
// `code="5"` means an error/not found. This is Washington-only; out-of-state ZIPs return no usable rate.
public class WaDorSalesTaxRateLookup(HttpClient http) : ISalesTaxRateLookup
{
    public async Task<SalesTaxRate?> LookupAsync(string? address, string? city, string zip, CancellationToken ct = default)
    {
        var url = $"webapi/AddressRates.aspx?output=xml" +
                  $"&addr={Uri.EscapeDataString(address ?? string.Empty)}" +
                  $"&city={Uri.EscapeDataString(city ?? string.Empty)}" +
                  $"&zip={Uri.EscapeDataString(zip)}";

        using var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var xml = await response.Content.ReadAsStringAsync(ct);
        return Parse(xml);
    }

    // Parses the DOR XML. Exposed for unit testing without a network call.
    public static SalesTaxRate? Parse(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        var response = doc.Root;
        if (response is null)
        {
            return null;
        }

        var code = (string?)response.Attribute("code");
        var combinedText = (string?)response.Attribute("rate");
        if (code == "5" || string.IsNullOrWhiteSpace(combinedText))
        {
            return null;
        }

        var rateElement = response.Element("rate");
        var combined = ParseRate(combinedText);
        if (combined <= 0m)
        {
            return null;
        }

        var state = ParseRate((string?)rateElement?.Attribute("staterate"));
        var local = ParseRate((string?)response.Attribute("localrate") ?? (string?)rateElement?.Attribute("localrate"));
        var name = (string?)rateElement?.Attribute("name") ?? "Washington";

        return new SalesTaxRate(name, combined, state, local);
    }

    private static decimal ParseRate(string? text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate) ? rate : 0m;
}
