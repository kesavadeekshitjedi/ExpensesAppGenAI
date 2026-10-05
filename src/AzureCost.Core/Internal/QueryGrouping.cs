namespace AzureCost.Core.Internal;

/// <summary>A single parsed row from a Cost Management query: a group key and its cost.</summary>
internal readonly record struct CostRow(string Key, decimal Cost, string Currency);
