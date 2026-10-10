using System.Collections.ObjectModel;
using AzureCost.Core;
using AzureCost.Core.Abstractions;
using AzureCost.Core.Models;
using AzureCost_WinUI.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace AzureCost_WinUI.ViewModels;

/// <summary>
/// Drives the whole window. It depends only on <see cref="IAzureCostService"/> — the exact
/// same contract the console app uses — and exposes observable collections the XAML binds to.
///
/// All UI state is mutated on the UI thread via <see cref="_dispatcher"/>: the service calls
/// complete on the thread pool, so every post-await update is marshalled back, because touching
/// a bound <see cref="ObservableCollection{T}"/> (or raising PropertyChanged) off the UI thread
/// crashes WinUI.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    /// <summary>How long to coalesce rapid selection changes before actually fetching, so
    /// flicking through subscriptions/periods/resource groups fires one query, not a burst
    /// (which Cost Management throttles with 429 — see docs/ISSUES.md #1).</summary>
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(300);

    private readonly IAzureCostService _costService;
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    private bool _suppressReload;
    private int _costLoadToken;
    private int _resourceLoadToken;
    private CancellationTokenSource? _costDebounceCts;
    private CancellationTokenSource? _resourceDebounceCts;

    public MainViewModel(IAzureCostService costService)
    {
        _costService = costService;
        Periods = new ObservableCollection<CostPeriod>(CostPeriod.Presets);
        SelectedPeriod = CostPeriod.MonthToDate;
    }

    public ObservableCollection<SubscriptionInfo> Subscriptions { get; } = new();

    public ObservableCollection<CostPeriod> Periods { get; }

    public ObservableCollection<ResourceGroupRow> ResourceGroups { get; } = new();

    public ObservableCollection<ResourceRow> Resources { get; } = new();

    [ObservableProperty]
    private SubscriptionInfo? _selectedSubscription;

    [ObservableProperty]
    private CostPeriod _selectedPeriod;

    [ObservableProperty]
    private ResourceGroupRow? _selectedResourceGroup;

    [ObservableProperty]
    private string _totalCostDisplay = "—";

    [ObservableProperty]
    private string _subscriptionCaption = "No subscription selected";

    [ObservableProperty]
    private string _resourcesHeader = "Select a resource group";

    [ObservableProperty]
    private string _resourcesSubheader = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyLoading))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyLoading))]
    private bool _isLoadingResources;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoResourceGroups))]
    private bool _hasLoadedOnce;

    /// <summary>True while any fetch is in flight (subscription/period costs or resources),
    /// so a single top-of-window progress bar can indicate all of them.</summary>
    public bool IsAnyLoading => IsBusy || IsLoadingResources;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasNoResourceGroups => HasLoadedOnce && ResourceGroups.Count == 0;

    /// <summary>Loads subscriptions and the first subscription's costs.</summary>
    [RelayCommand]
    private async Task InitializeAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var subs = await _costService.GetSubscriptionsAsync();

            await RunOnUiAsync(() =>
            {
                _suppressReload = true;
                Subscriptions.Clear();
                foreach (var s in subs)
                {
                    Subscriptions.Add(s);
                }

                SelectedSubscription = Subscriptions.FirstOrDefault();
                _suppressReload = false;
            });

            if (SelectedSubscription is null)
            {
                await RunOnUiAsync(() =>
                    ErrorMessage = "No subscriptions are visible to the signed-in identity.");
                return;
            }

            await LoadCostsAsync();
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                _suppressReload = false;
                ErrorMessage = Describe(ex);
            });
        }
        finally
        {
            await RunOnUiAsync(() => IsBusy = false);
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadCostsAsync();

    partial void OnSelectedSubscriptionChanged(SubscriptionInfo? value)
    {
        if (!_suppressReload && value is not null)
        {
            _ = DebouncedLoadCostsAsync();
        }
    }

    partial void OnSelectedPeriodChanged(CostPeriod value)
    {
        if (!_suppressReload)
        {
            _ = DebouncedLoadCostsAsync();
        }
    }

    partial void OnSelectedResourceGroupChanged(ResourceGroupRow? value)
    {
        if (!_suppressReload)
        {
            _ = DebouncedLoadResourcesAsync(value);
        }
    }

    /// <summary>Waits out a quiet <see cref="DebounceInterval"/> before loading costs; a newer
    /// selection cancels this one, so a flurry of changes only fetches the final selection.</summary>
    private async Task DebouncedLoadCostsAsync()
    {
        // Field writes happen on the UI thread (the only caller), so no locking is needed.
        _costDebounceCts?.Cancel();
        _costDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _costDebounceCts = cts;

        if (await QuietAsync(cts.Token).ConfigureAwait(false))
        {
            await LoadCostsAsync().ConfigureAwait(false);
        }
    }

    private async Task DebouncedLoadResourcesAsync(ResourceGroupRow? group)
    {
        _resourceDebounceCts?.Cancel();
        _resourceDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _resourceDebounceCts = cts;

        if (await QuietAsync(cts.Token).ConfigureAwait(false))
        {
            await LoadResourcesAsync(group).ConfigureAwait(false);
        }
    }

    /// <summary>Returns true if the debounce window elapsed undisturbed, or false if a newer
    /// change cancelled it (in which case the caller should not fetch).</summary>
    private static async Task<bool> QuietAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(DebounceInterval, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task LoadCostsAsync()
    {
        var subscription = SelectedSubscription;
        if (subscription is null)
        {
            return;
        }

        var token = ++_costLoadToken;
        var period = SelectedPeriod;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var summary = await _costService.GetSubscriptionCostAsync(
                subscription.SubscriptionId, period);

            await RunOnUiAsync(() =>
            {
                if (token != _costLoadToken)
                {
                    return; // a newer load superseded this one
                }

                TotalCostDisplay = Format.Money(summary.TotalCost, summary.Currency);
                SubscriptionCaption = $"{subscription.DisplayName}  ·  {period.Label}";

                _suppressReload = true;
                Resources.Clear();
                SelectedResourceGroup = null;
                ResourceGroups.Clear();
                foreach (var g in summary.ResourceGroups)
                {
                    ResourceGroups.Add(new ResourceGroupRow(
                        g.Name,
                        g.Cost,
                        Format.Money(g.Cost, g.Currency),
                        g.ShareOfTotal,
                        Format.Percent(g.ShareOfTotal)));
                }

                _suppressReload = false;

                ResourcesHeader = "Select a resource group";
                ResourcesSubheader = string.Empty;
                HasLoadedOnce = true;
                OnPropertyChanged(nameof(HasNoResourceGroups));
            });
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                _suppressReload = false;
                if (token == _costLoadToken)
                {
                    ErrorMessage = Describe(ex);
                }
            });
        }
        finally
        {
            if (token == _costLoadToken)
            {
                await RunOnUiAsync(() => IsBusy = false);
            }
        }
    }

    private async Task LoadResourcesAsync(ResourceGroupRow? group)
    {
        var token = ++_resourceLoadToken;
        var subscription = SelectedSubscription;

        await RunOnUiAsync(() => Resources.Clear());

        if (group is null || subscription is null)
        {
            await RunOnUiAsync(() =>
            {
                ResourcesHeader = "Select a resource group";
                ResourcesSubheader = string.Empty;
            });
            return;
        }

        await RunOnUiAsync(() =>
        {
            ResourcesHeader = group.Name;
            ResourcesSubheader = "Loading…";
            IsLoadingResources = true;
            ErrorMessage = null;
        });

        try
        {
            var resources = await _costService.GetResourceCostsAsync(
                subscription.SubscriptionId, group.Name, SelectedPeriod);

            await RunOnUiAsync(() =>
            {
                if (token != _resourceLoadToken)
                {
                    return;
                }

                foreach (var r in resources)
                {
                    Resources.Add(new ResourceRow(
                        r.Name,
                        r.ResourceType,
                        Format.Money(r.Cost, r.Currency),
                        r.ShareOfTotal,
                        Format.Percent(r.ShareOfTotal)));
                }

                ResourcesSubheader = resources.Count == 0
                    ? "No per-resource cost reported for this period."
                    : $"{resources.Count} resource(s)  ·  {group.CostDisplay}";
            });
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                if (token == _resourceLoadToken)
                {
                    ResourcesSubheader = string.Empty;
                    ErrorMessage = Describe(ex);
                }
            });
        }
        finally
        {
            if (token == _resourceLoadToken)
            {
                await RunOnUiAsync(() => IsLoadingResources = false);
            }
        }
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread, awaiting its completion.</summary>
    private Task RunOnUiAsync(Action action)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource();
        var queued = dispatcher.TryEnqueue(() =>
        {
            try
            {
                action();
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        if (!queued)
        {
            tcs.TrySetException(new InvalidOperationException("Could not dispatch to the UI thread."));
        }

        return tcs.Task;
    }

    private static string Describe(Exception ex) =>
        ex is AzureCostException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
}

/// <summary>Formatting helpers kept out of the view model body for reuse/readability.</summary>
internal static class Format
{
    public static string Money(decimal amount, string currency) =>
        string.IsNullOrEmpty(currency) ? amount.ToString("N2") : $"{amount:N2} {currency}";

    public static string Percent(double share) => share.ToString("P0");
}
