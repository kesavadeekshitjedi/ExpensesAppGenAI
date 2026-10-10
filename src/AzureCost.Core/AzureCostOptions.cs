using Azure.Core;

namespace AzureCost.Core;

/// <summary>
/// Configuration for the Azure cost stack. Everything has a sensible default so a
/// front-end can call <c>AddAzureCost()</c> with no arguments and get working behaviour,
/// while still allowing a WinUI app to supply its own <see cref="Credential"/>
/// (e.g. an interactive broker credential) without changing any other code.
/// </summary>
public sealed class AzureCostOptions
{
    /// <summary>Azure Resource Manager base URI (change for sovereign clouds).</summary>
    public string ArmBaseUri { get; set; } = "https://management.azure.com";

    /// <summary>API version for the Cost Management query endpoint.</summary>
    public string CostApiVersion { get; set; } = "2023-11-01";

    /// <summary>API version for the subscriptions list endpoint.</summary>
    public string SubscriptionsApiVersion { get; set; } = "2022-12-01";

    /// <summary>
    /// Name of the cost metric to aggregate. "Cost" works for pay-as-you-go /
    /// Microsoft Customer Agreement; switch to "PreTaxCost" for some EA offers.
    /// </summary>
    public string CostMetric { get; set; } = "Cost";

    /// <summary>
    /// Credential used for all ARM calls. When null, a <c>DefaultAzureCredential</c>
    /// is created (honours <c>az login</c>, Visual Studio, environment vars, managed identity).
    /// </summary>
    public TokenCredential? Credential { get; set; }

    /// <summary>Optional tenant to scope the default credential to.</summary>
    public string? TenantId { get; set; }

    /// <summary>OAuth scope requested for ARM tokens.</summary>
    public string ArmScope { get; set; } = "https://management.azure.com/.default";

    /// <summary>
    /// How many times to retry a throttled (HTTP 429) or transient (502/503/504) ARM
    /// response before surfacing the error. Cost Management throttles aggressively when the
    /// UI fires bursts of queries (rapid subscription / period / resource-group changes), so
    /// a few retries with backoff turn those 429s into a brief wait instead of a visible error.
    /// Set to 0 to disable retrying.
    /// </summary>
    public int MaxRetries { get; set; } = 4;

    /// <summary>
    /// Base delay for exponential backoff when the server doesn't send a <c>Retry-After</c>
    /// header. Attempt <c>n</c> waits roughly <c>RetryBaseDelay * 2^(n-1)</c> (plus jitter).
    /// A server-supplied <c>Retry-After</c> always takes precedence over this.
    /// </summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);
}
