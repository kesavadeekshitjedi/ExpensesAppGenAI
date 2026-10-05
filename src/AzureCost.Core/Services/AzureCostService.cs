using AzureCost.Core.Abstractions;
using AzureCost.Core.Internal;
using AzureCost.Core.Models;

namespace AzureCost.Core.Services;

/// <summary>
/// Default <see cref="IAzureCostService"/>. Orchestrates ARM queries and shapes the raw
/// rows into the presentation-agnostic models the UI binds to. No UI dependencies here.
/// </summary>
internal sealed class AzureCostService : IAzureCostService
{
    private readonly ArmClient _arm;

    public AzureCostService(ArmClient arm) => _arm = arm;

    public Task<IReadOnlyList<SubscriptionInfo>> GetSubscriptionsAsync(CancellationToken cancellationToken = default)
        => _arm.ListSubscriptionsAsync(cancellationToken);

    public async Task<SubscriptionCostSummary> GetSubscriptionCostAsync(
        string subscriptionId, CostPeriod period, CancellationToken cancellationToken = default)
    {
        var groups = await GetResourceGroupCostsAsync(subscriptionId, period, cancellationToken)
            .ConfigureAwait(false);

        var total = groups.Sum(g => g.Cost);
        var currency = groups.FirstOrDefault(g => !string.IsNullOrEmpty(g.Currency))?.Currency ?? "";

        return new SubscriptionCostSummary(subscriptionId, period, total, currency, groups);
    }

    public async Task<IReadOnlyList<ResourceGroupCost>> GetResourceGroupCostsAsync(
        string subscriptionId, CostPeriod period, CancellationToken cancellationToken = default)
    {
        var scope = $"/subscriptions/{subscriptionId}";
        var rows = await _arm
            .QueryGroupedCostAsync(scope, "ResourceGroupName", period, cancellationToken)
            .ConfigureAwait(false);

        var total = rows.Sum(r => r.Cost);
        return rows
            .Select(r => new ResourceGroupCost(r.Key, r.Cost, r.Currency)
            {
                ShareOfTotal = total > 0 ? (double)(r.Cost / total) : 0,
            })
            .OrderByDescending(r => r.Cost)
            .ToList();
    }

    public async Task<IReadOnlyList<ResourceCost>> GetResourceCostsAsync(
        string subscriptionId, string resourceGroupName, CostPeriod period,
        CancellationToken cancellationToken = default)
    {
        var scope = $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}";
        var rows = await _arm
            .QueryGroupedCostAsync(scope, "ResourceId", period, cancellationToken)
            .ConfigureAwait(false);

        var total = rows.Sum(r => r.Cost);
        return rows
            .Select(r =>
            {
                var (name, type) = ResourceIdParser.Parse(r.Key);
                return new ResourceCost(r.Key, name, type, r.Cost, r.Currency)
                {
                    ShareOfTotal = total > 0 ? (double)(r.Cost / total) : 0,
                };
            })
            .OrderByDescending(r => r.Cost)
            .ToList();
    }
}
