using AzureCost.Core.Models;

namespace AzureCost.Core.Abstractions;

/// <summary>
/// The single contract every front-end (console today, WinUI 3 tomorrow) depends on.
/// Implementations return plain data models and never touch the console, so a view
/// model can inject this interface and bind the results directly.
/// </summary>
public interface IAzureCostService
{
    /// <summary>Lists subscriptions visible to the signed-in identity.</summary>
    Task<IReadOnlyList<SubscriptionInfo>> GetSubscriptionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the subscription total for the period together with the per-resource-group
    /// breakdown (sorted by cost, descending) in one call.
    /// </summary>
    Task<SubscriptionCostSummary> GetSubscriptionCostAsync(
        string subscriptionId,
        CostPeriod period,
        CancellationToken cancellationToken = default);

    /// <summary>Gets cost per resource group for a subscription, sorted by cost descending.</summary>
    Task<IReadOnlyList<ResourceGroupCost>> GetResourceGroupCostsAsync(
        string subscriptionId,
        CostPeriod period,
        CancellationToken cancellationToken = default);

    /// <summary>Drills into a resource group: cost per individual resource, sorted descending.</summary>
    Task<IReadOnlyList<ResourceCost>> GetResourceCostsAsync(
        string subscriptionId,
        string resourceGroupName,
        CostPeriod period,
        CancellationToken cancellationToken = default);
}
