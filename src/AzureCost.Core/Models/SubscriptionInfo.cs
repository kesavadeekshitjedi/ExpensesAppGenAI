namespace AzureCost.Core.Models;

/// <summary>An Azure subscription the signed-in identity can see.</summary>
public sealed record SubscriptionInfo(
    string SubscriptionId,
    string DisplayName,
    string State,
    string? TenantId)
{
    public override string ToString() => $"{DisplayName} ({SubscriptionId})";
}
