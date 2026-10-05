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
}
