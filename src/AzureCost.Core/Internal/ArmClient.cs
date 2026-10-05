using System.Net;
using System.Text;
using System.Text.Json;
using AzureCost.Core.Models;
using Microsoft.Extensions.Options;

namespace AzureCost.Core.Internal;

/// <summary>
/// Thin wrapper over the ARM REST surface we need: listing subscriptions and running
/// Cost Management queries. Keeps all HTTP/JSON concerns out of the higher-level service.
/// </summary>
internal sealed class ArmClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly AzureCostOptions _options;

    public ArmClient(HttpClient http, IOptions<AzureCostOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(CancellationToken ct)
    {
        var url = $"/subscriptions?api-version={_options.SubscriptionsApiVersion}";
        using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);

        var result = new List<SubscriptionInfo>();
        if (doc.RootElement.TryGetProperty("value", out var value))
        {
            foreach (var item in value.EnumerateArray())
            {
                result.Add(new SubscriptionInfo(
                    SubscriptionId: item.GetStringOrEmpty("subscriptionId"),
                    DisplayName: item.GetStringOrEmpty("displayName"),
                    State: item.GetStringOrEmpty("state"),
                    TenantId: item.TryGetProperty("tenantId", out var t) ? t.GetString() : null));
            }
        }

        return result
            .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Runs a grouped Cost Management query at <paramref name="scope"/>, grouping by the
    /// given dimension (e.g. "ResourceGroupName" or "ResourceId").
    /// </summary>
    public async Task<IReadOnlyList<CostRow>> QueryGroupedCostAsync(
        string scope, string groupingDimension, CostPeriod period, CancellationToken ct)
    {
        var url = $"{scope}/providers/Microsoft.CostManagement/query?api-version={_options.CostApiVersion}";
        var body = BuildQueryBody(groupingDimension, period);

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        var payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AzureCostException(DescribeError(response.StatusCode, payload));
        }

        using var doc = JsonDocument.Parse(payload);
        return ParseRows(doc, groupingDimension);
    }

    private string BuildQueryBody(string groupingDimension, CostPeriod period)
    {
        var dataset = new Dictionary<string, object?>
        {
            ["granularity"] = "None",
            ["aggregation"] = new Dictionary<string, object?>
            {
                ["totalCost"] = new { name = _options.CostMetric, function = "Sum" },
            },
            ["grouping"] = new object[]
            {
                new { type = "Dimension", name = groupingDimension },
            },
        };

        var query = new Dictionary<string, object?>
        {
            ["type"] = "ActualCost",
            ["timeframe"] = period.Timeframe,
            ["dataset"] = dataset,
        };

        if (period.IsCustom && period.From is { } from && period.To is { } to)
        {
            query["timePeriod"] = new
            {
                from = from.ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                to = to.ToDateTime(new TimeOnly(23, 59, 59)).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            };
        }

        return JsonSerializer.Serialize(query, JsonOptions);
    }

    /// <summary>
    /// Cost Management returns dynamic columns + rows. We locate columns by name so the
    /// parser is resilient to ordering changes between API versions.
    /// </summary>
    private static IReadOnlyList<CostRow> ParseRows(JsonDocument doc, string groupingDimension)
    {
        if (!doc.RootElement.TryGetProperty("properties", out var props) ||
            !props.TryGetProperty("columns", out var columns) ||
            !props.TryGetProperty("rows", out var rows))
        {
            return [];
        }

        int costIndex = -1, keyIndex = -1, currencyIndex = -1;
        var colNames = columns.EnumerateArray().Select(c => c.GetStringOrEmpty("name")).ToArray();
        for (var i = 0; i < colNames.Length; i++)
        {
            var name = colNames[i];
            if (name.Equals("Cost", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("PreTaxCost", StringComparison.OrdinalIgnoreCase))
            {
                costIndex = i;
            }
            else if (name.Equals(groupingDimension, StringComparison.OrdinalIgnoreCase) ||
                     name.Equals("ResourceGroup", StringComparison.OrdinalIgnoreCase) ||
                     name.Equals("ResourceId", StringComparison.OrdinalIgnoreCase))
            {
                keyIndex = i;
            }
            else if (name.Equals("Currency", StringComparison.OrdinalIgnoreCase))
            {
                currencyIndex = i;
            }
        }

        if (costIndex < 0 || keyIndex < 0)
        {
            return [];
        }

        var result = new List<CostRow>();
        foreach (var row in rows.EnumerateArray())
        {
            var cells = row.EnumerateArray().ToArray();
            if (costIndex >= cells.Length || keyIndex >= cells.Length)
            {
                continue;
            }

            var cost = cells[costIndex].ValueKind == JsonValueKind.Number
                ? cells[costIndex].GetDecimal()
                : 0m;
            var key = cells[keyIndex].ValueKind == JsonValueKind.String
                ? cells[keyIndex].GetString() ?? string.Empty
                : cells[keyIndex].ToString();
            var currency = currencyIndex >= 0 && currencyIndex < cells.Length
                ? cells[currencyIndex].GetString() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(key))
            {
                key = "(unassigned)";
            }

            result.Add(new CostRow(key, cost, currency));
        }

        return result;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AzureCostException(DescribeError(response.StatusCode, payload));
        }

        return JsonDocument.Parse(payload);
    }

    private static string DescribeError(HttpStatusCode status, string payload)
    {
        var detail = payload;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
            {
                detail = msg.GetString() ?? payload;
            }
        }
        catch (JsonException)
        {
            // keep raw payload
        }

        var hint = status switch
        {
            HttpStatusCode.Unauthorized => " (not signed in — run 'az login')",
            HttpStatusCode.Forbidden => " (signed in, but the identity lacks Cost Management Reader on this scope)",
            HttpStatusCode.TooManyRequests => " (throttled by Cost Management — retry shortly)",
            _ => string.Empty,
        };

        return $"Azure request failed ({(int)status} {status}){hint}: {detail}";
    }
}
