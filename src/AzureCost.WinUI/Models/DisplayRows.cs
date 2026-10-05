namespace AzureCost_WinUI.Models;

/// <summary>
/// Presentation rows: Core returns raw numbers; the view model formats them here so the
/// XAML can bind plain strings and a 0..1 <see cref="Share"/> straight onto a ProgressBar.
/// </summary>
public sealed record ResourceGroupRow(
    string Name,
    decimal Cost,
    string CostDisplay,
    double Share,
    string ShareDisplay);

public sealed record ResourceRow(
    string Name,
    string ResourceType,
    string CostDisplay,
    double Share,
    string ShareDisplay);
