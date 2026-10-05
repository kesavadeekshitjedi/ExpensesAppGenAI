namespace AzureCost.Core.Internal;

/// <summary>
/// Pulls a friendly name and resource type out of a full ARM resource id, e.g.
/// /subscriptions/s/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/myvm
/// -> name "myvm", type "Microsoft.Compute/virtualMachines".
/// </summary>
internal static class ResourceIdParser
{
    public static (string Name, string ResourceType) Parse(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return ("(unknown)", "");
        }

        var segments = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var providersIdx = Array.FindIndex(segments,
            s => s.Equals("providers", StringComparison.OrdinalIgnoreCase));

        if (providersIdx < 0 || providersIdx + 2 >= segments.Length)
        {
            return (segments.Length > 0 ? segments[^1] : resourceId, "");
        }

        var name = segments[^1];

        // provider namespace + alternating type/name pairs after "providers"
        var provider = segments[providersIdx + 1];
        var typeParts = new List<string> { provider };
        for (var i = providersIdx + 2; i < segments.Length; i += 2)
        {
            typeParts.Add(segments[i]);
        }

        return (name, string.Join('/', typeParts));
    }
}
