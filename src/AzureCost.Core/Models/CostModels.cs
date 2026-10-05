namespace AzureCost.Core.Models;

/// <summary>Total spend for a subscription over a period, plus a per-resource-group breakdown.</summary>
public sealed record SubscriptionCostSummary(
    string SubscriptionId,
    CostPeriod Period,
    decimal TotalCost,
    string Currency,
    IReadOnlyList<ResourceGroupCost> ResourceGroups)
{
    public static SubscriptionCostSummary Empty(string subscriptionId, CostPeriod period) =>
        new(subscriptionId, period, 0m, "", []);
}

/// <summary>Spend attributed to a single resource group.</summary>
public sealed record ResourceGroupCost(
    string Name,
    decimal Cost,
    string Currency)
{
    /// <summary>Share of the parent total (0..1); set by the service once totals are known.</summary>
    public double ShareOfTotal { get; init; }
}

/// <summary>Spend attributed to a single resource inside a resource group.</summary>
public sealed record ResourceCost(
    string ResourceId,
    string Name,
    string ResourceType,
    decimal Cost,
    string Currency)
{
    public double ShareOfTotal { get; init; }
}
