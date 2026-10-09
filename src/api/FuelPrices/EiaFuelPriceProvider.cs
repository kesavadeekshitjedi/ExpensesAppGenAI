using System.Globalization;
using System.Text.Json;

namespace Expenses.Api.FuelPrices;

// Reads the latest weekly regular-gasoline price from the U.S. Energy Information Administration (EIA)
// open-data API v2. The key is a free registration key, supplied from configuration (Key Vault in prod).
// Response shape: { "response": { "data": [ { "period": "...", "area-name": "...", "value": 5.424 } ] } }.
public class EiaFuelPriceProvider(HttpClient http, string apiKey) : IFuelPriceProvider
{
    public async Task<FuelPrice?> GetLatestAsync(string area, CancellationToken ct = default)
    {
        var url = $"v2/petroleum/pri/gnd/data/?api_key={Uri.EscapeDataString(apiKey)}" +
                  "&frequency=weekly&data[0]=value&facets[product][]=EPMR" +
                  $"&facets[duoarea][]={Uri.EscapeDataString(area)}" +
                  "&sort[0][column]=period&sort[0][direction]=desc&length=1";

        using var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("response", out var resp) ||
            !resp.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            return null;
        }

        var row = data[0];
        var period = row.TryGetProperty("period", out var p) ? p.GetString() ?? string.Empty : string.Empty;
        var areaName = row.TryGetProperty("area-name", out var a) ? a.GetString() ?? area : area;

        if (!row.TryGetProperty("value", out var v))
        {
            return null;
        }
        var value = v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDecimal(),
            JsonValueKind.String => decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m,
            _ => 0m,
        };

        return value > 0m ? new FuelPrice(areaName, period, value) : null;
    }
}
