using System.Text.Json;

namespace AzureCost.Core.Internal;

internal static class JsonElementExtensions
{
    public static string GetStringOrEmpty(this JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }
}
