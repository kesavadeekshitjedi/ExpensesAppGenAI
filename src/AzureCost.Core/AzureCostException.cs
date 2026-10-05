namespace AzureCost.Core;

/// <summary>Thrown when an ARM / Cost Management call fails in a way worth surfacing to the user.</summary>
public sealed class AzureCostException : Exception
{
    public AzureCostException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
