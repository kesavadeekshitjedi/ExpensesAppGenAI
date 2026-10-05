using AzureCost.Core;
using AzureCost.Core.Abstractions;
using AzureCost.Core.Models;
using Spectre.Console;

namespace AzureCost.Console.Ui;

/// <summary>
/// The console presentation layer. It only talks to <see cref="IAzureCostService"/> and
/// renders the returned models — a WinUI 3 view model would consume the same service the
/// same way, just binding instead of printing.
/// </summary>
public sealed class ConsoleCostExplorer
{
    private readonly IAzureCostService _costService;

    public ConsoleCostExplorer(IAzureCostService costService) => _costService = costService;

    public async Task RunAsync(CancellationToken ct)
    {
        AnsiConsole.Write(
            new FigletText("Azure Cost").Color(Color.DodgerBlue1));
        AnsiConsole.MarkupLine("[grey]Explore subscription spend and drill into resource groups.[/]");
        AnsiConsole.WriteLine();

        SubscriptionInfo subscription;
        try
        {
            subscription = await SelectSubscriptionAsync(ct).ConfigureAwait(false);
        }
        catch (AzureCostException ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{ex.Message}[/]");
            return;
        }

        var period = CostPeriod.MonthToDate;

        while (!ct.IsCancellationRequested)
        {
            SubscriptionCostSummary summary;
            try
            {
                summary = await LoadWithSpinnerAsync(
                    $"Loading costs for [yellow]{Markup.Escape(subscription.DisplayName)}[/] ({period.Label})…",
                    () => _costService.GetSubscriptionCostAsync(subscription.SubscriptionId, period, ct))
                    .ConfigureAwait(false);
            }
            catch (AzureCostException ex)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]{ex.Message}[/]");
                if (!AnsiConsole.Confirm("Try again?"))
                {
                    return;
                }

                continue;
            }

            RenderSubscriptionSummary(subscription, summary);

            var action = PromptTopLevelAction(summary);
            switch (action.Kind)
            {
                case MenuKind.DrillIn:
                    await ShowResourcesAsync(subscription, action.ResourceGroup!, period, ct)
                        .ConfigureAwait(false);
                    break;
                case MenuKind.ChangePeriod:
                    period = PromptForPeriod(period);
                    break;
                case MenuKind.ChangeSubscription:
                    subscription = await SelectSubscriptionAsync(ct).ConfigureAwait(false);
                    period = CostPeriod.MonthToDate;
                    break;
                case MenuKind.Quit:
                    return;
            }
        }
    }

    private async Task<SubscriptionInfo> SelectSubscriptionAsync(CancellationToken ct)
    {
        var subscriptions = await LoadWithSpinnerAsync(
            "Discovering subscriptions…",
            () => _costService.GetSubscriptionsAsync(ct)).ConfigureAwait(false);

        if (subscriptions.Count == 0)
        {
            throw new AzureCostException(
                "No subscriptions are visible to the signed-in identity. Run 'az login' and verify access.");
        }

        if (subscriptions.Count == 1)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[grey]Using the only visible subscription:[/] [yellow]{subscriptions[0].DisplayName}[/]");
            return subscriptions[0];
        }

        return AnsiConsole.Prompt(
            new SelectionPrompt<SubscriptionInfo>()
                .Title("Select a [green]subscription[/]:")
                .PageSize(15)
                .UseConverter(s => $"{s.DisplayName}  [grey]{s.SubscriptionId}[/]")
                .AddChoices(subscriptions));
    }

    private async Task ShowResourcesAsync(
        SubscriptionInfo subscription, ResourceGroupCost rg, CostPeriod period, CancellationToken ct)
    {
        IReadOnlyList<ResourceCost> resources;
        try
        {
            resources = await LoadWithSpinnerAsync(
                $"Loading resources in [yellow]{Markup.Escape(rg.Name)}[/]…",
                () => _costService.GetResourceCostsAsync(
                    subscription.SubscriptionId, rg.Name, period, ct)).ConfigureAwait(false);
        }
        catch (AzureCostException ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{ex.Message}[/]");
            return;
        }

        var currency = resources.FirstOrDefault(r => !string.IsNullOrEmpty(r.Currency))?.Currency
            ?? rg.Currency;

        var header = new Rule($"[green]{Markup.Escape(rg.Name)}[/]  ·  {FormatMoney(rg.Cost, currency)}  ·  {period.Label}")
        {
            Justification = Justify.Left,
        };
        AnsiConsole.Write(header);

        if (resources.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No per-resource cost reported for this period " +
                "(the group may hold free resources, or spend hasn't been rated yet).[/]");
        }
        else
        {
            var table = new Table().Border(TableBorder.Rounded).Expand();
            table.AddColumn("Resource");
            table.AddColumn("Type");
            table.AddColumn(new TableColumn("Cost").RightAligned());
            table.AddColumn(new TableColumn("Share").RightAligned());

            foreach (var r in resources)
            {
                table.AddRow(
                    $"[white]{Markup.Escape(r.Name)}[/]",
                    $"[grey]{Markup.Escape(r.ResourceType)}[/]",
                    FormatMoney(r.Cost, currency),
                    ShareBar(r.ShareOfTotal));
            }

            AnsiConsole.Write(table);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Confirm("[grey]Press Enter to go back[/]", defaultValue: true);
    }

    private static void RenderSubscriptionSummary(SubscriptionInfo subscription, SubscriptionCostSummary summary)
    {
        AnsiConsole.WriteLine();
        var panel = new Panel(new Markup(
            $"[bold]{Markup.Escape(subscription.DisplayName)}[/]\n" +
            $"[grey]{subscription.SubscriptionId}[/]\n\n" +
            $"Total ([yellow]{summary.Period.Label}[/]): " +
            $"[bold green]{FormatMoney(summary.TotalCost, summary.Currency)}[/]"))
        {
            Header = new PanelHeader(" Subscription "),
            Border = BoxBorder.Rounded,
            Padding = new Padding(2, 1),
        };
        AnsiConsole.Write(panel);

        if (summary.ResourceGroups.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No resource-group costs for this period.[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.AddColumn("Resource group");
        table.AddColumn(new TableColumn("Cost").RightAligned());
        table.AddColumn(new TableColumn("Share").RightAligned());

        foreach (var g in summary.ResourceGroups)
        {
            table.AddRow(
                $"[white]{Markup.Escape(g.Name)}[/]",
                FormatMoney(g.Cost, summary.Currency),
                ShareBar(g.ShareOfTotal));
        }

        AnsiConsole.Write(table);
    }

    private static TopLevelAction PromptTopLevelAction(SubscriptionCostSummary summary)
    {
        const string drillPrefix = "Drill into  ";
        const string changePeriod = "↻  Change period";
        const string changeSub = "⇄  Change subscription";
        const string quit = "✕  Quit";

        var choices = new List<string>();
        var map = new Dictionary<string, ResourceGroupCost>();
        foreach (var g in summary.ResourceGroups)
        {
            var label = $"{drillPrefix}{g.Name}";
            choices.Add(label);
            map[label] = g;
        }

        choices.Add(changePeriod);
        choices.Add(changeSub);
        choices.Add(quit);

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("\nWhat next?")
                .PageSize(18)
                .MoreChoicesText("[grey](scroll for more)[/]")
                .UseConverter(Markup.Escape)
                .AddChoices(choices));

        return selected switch
        {
            changePeriod => new TopLevelAction(MenuKind.ChangePeriod),
            changeSub => new TopLevelAction(MenuKind.ChangeSubscription),
            quit => new TopLevelAction(MenuKind.Quit),
            _ => new TopLevelAction(MenuKind.DrillIn, map[selected]),
        };
    }

    private static CostPeriod PromptForPeriod(CostPeriod current)
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<CostPeriod>()
                .Title($"Current period: [yellow]{current.Label}[/]. Choose a period:")
                .UseConverter(p => p.Label)
                .AddChoices(CostPeriod.Presets));
    }

    private static async Task<T> LoadWithSpinnerAsync<T>(string message, Func<Task<T>> operation)
    {
        T result = default!;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync(message, async _ => result = await operation().ConfigureAwait(false))
            .ConfigureAwait(false);
        return result;
    }

    private static string FormatMoney(decimal amount, string currency) =>
        string.IsNullOrEmpty(currency) ? amount.ToString("N2") : $"{amount:N2} {currency}";

    private static string ShareBar(double share)
    {
        var pct = Math.Clamp(share, 0, 1);
        const int width = 12;
        var filled = (int)Math.Round(pct * width);
        var bar = new string('█', filled) + new string('░', width - filled);
        var color = pct switch
        {
            >= 0.5 => "red",
            >= 0.2 => "yellow",
            _ => "green",
        };
        return $"[{color}]{bar}[/] {pct:P0}";
    }

    private enum MenuKind
    {
        DrillIn,
        ChangePeriod,
        ChangeSubscription,
        Quit,
    }

    private sealed record TopLevelAction(MenuKind Kind, ResourceGroupCost? ResourceGroup = null);
}
