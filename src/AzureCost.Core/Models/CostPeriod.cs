namespace AzureCost.Core.Models;

/// <summary>
/// Describes the time window a cost query runs over. Maps onto the
/// Cost Management "timeframe" concept, with convenient presets that are
/// UI-agnostic so the same values can drive a console menu or a WinUI combo box.
/// </summary>
public sealed record CostPeriod
{
    private CostPeriod(string label, string timeframe, DateOnly? from = null, DateOnly? to = null)
    {
        Label = label;
        Timeframe = timeframe;
        From = from;
        To = to;
    }

    /// <summary>Human-friendly name, e.g. "Month to date".</summary>
    public string Label { get; }

    /// <summary>The Cost Management timeframe token, e.g. "MonthToDate" or "Custom".</summary>
    public string Timeframe { get; }

    /// <summary>Inclusive start date (only set when <see cref="Timeframe"/> is "Custom").</summary>
    public DateOnly? From { get; }

    /// <summary>Inclusive end date (only set when <see cref="Timeframe"/> is "Custom").</summary>
    public DateOnly? To { get; }

    public bool IsCustom => Timeframe == "Custom";

    public static CostPeriod MonthToDate { get; } = new("Month to date", "MonthToDate");

    public static CostPeriod LastMonth { get; } = new("Last month", "TheLastMonth");

    public static CostPeriod Last7Days => CustomDays("Last 7 days", 7);

    public static CostPeriod Last30Days => CustomDays("Last 30 days", 30);

    public static CostPeriod Custom(DateOnly from, DateOnly to) =>
        new($"{from:yyyy-MM-dd} .. {to:yyyy-MM-dd}", "Custom", from, to);

    /// <summary>A window ending today (UTC) and spanning the given number of days.</summary>
    public static CostPeriod CustomDays(string label, int days)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new CostPeriod(label, "Custom", today.AddDays(-(days - 1)), today);
    }

    /// <summary>The presets a UI can bind to directly.</summary>
    public static IReadOnlyList<CostPeriod> Presets { get; } =
    [
        MonthToDate,
        LastMonth,
        Last7Days,
        Last30Days,
    ];

    public override string ToString() => Label;
}
