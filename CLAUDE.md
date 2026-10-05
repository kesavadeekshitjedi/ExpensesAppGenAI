# CLAUDE.md — Azure Cost Explorer

Context for AI assistants working on this repo. See `README.md` for user-facing docs.

## What this is
A C# solution that shows Azure spend and drills **subscription → resource group → resource**,
built so the Azure logic is a reusable core with pluggable front-ends (console + WinUI 3).

## Solution layout (`AzureCost.slnx`)
```
src/
  AzureCost.Core/      net9.0            — UI-agnostic: Cost Management client, models, DI
  AzureCost.Console/   net9.0            — Spectre.Console interactive explorer
  AzureCost.WinUI/     net10.0-windows   — rich master-detail WinUI 3 app
```

### AzureCost.Core (the heart — keep it UI-free)
- `Abstractions/IAzureCostService.cs` — the one contract every front-end uses. Returns plain
  records: `SubscriptionCostSummary`, `ResourceGroupCost`, `ResourceCost`, `SubscriptionInfo`.
- `Internal/ArmClient.cs` — calls the Cost Management **Query API**
  (`POST .../providers/Microsoft.CostManagement/query`, `ActualCost`, `Sum` of the metric),
  grouped by `ResourceGroupName` (subscription scope) then `ResourceId` (RG scope). Parses the
  dynamic columns/rows **by column name** so ordering changes don't break it.
- `Internal/BearerTokenHandler.cs` — adds the ARM bearer token to each request.
- `Services/AzureCostService.cs` — shapes rows into the public models, computes `ShareOfTotal`.
- `DependencyInjection/ServiceCollectionExtensions.cs` — `AddAzureCost(options => ...)`.
  Credential defaults to `DefaultAzureCredential`; override via `options.Credential`.
- `AzureCostOptions.cs` — `CostMetric` ("Cost"; use "PreTaxCost" for some EA offers), API
  versions, `ArmBaseUri` (sovereign clouds), `TenantId`.

### AzureCost.Console
Host + DI in `Program.cs` → `AddAzureCost()` → `ConsoleCostExplorer` (Spectre.Console).
**Verified against live Azure**: Pay-As-You-Go sub, ~$1.28 MTD, `rg-expenses-prod`.

### AzureCost.WinUI
- MVVM via CommunityToolkit.Mvvm. `App.xaml.cs` builds the DI container (same `AddAzureCost()`)
  using `ChainedTokenCredential(AzureCli, VisualStudio, InteractiveBrowser)` + a global
  exception logger → `%LOCALAPPDATA%\AzureCostWinUI\crash.log`.
- `MainPage.xaml` — toolbar (subscription + period pickers, Refresh), total-spend card,
  master-detail: resource groups (left) with share bars → resources (right).
- `ViewModels/MainViewModel.cs` — injects `IAzureCostService`. **All post-`await` UI updates are
  marshalled to the UI thread via `DispatcherQueue` (`RunOnUiAsync`)** with latest-wins tokens.
- `Models/DisplayRows.cs` — pre-formatted rows so XAML binds plain strings + a 0..1 `Share`.

## How to run
- **Console:** `az login` first, then `dotnet run --project src/AzureCost.Console`.
  Needs **Cost Management Reader** (or higher) on the subscription.
- **WinUI:** VS 2026 (or .NET 10 SDK). Build **x64**, launch profile
  **"AzureCost.WinUI (Unpackaged)"**. CLI:
  `dotnet run --project src/AzureCost.WinUI -p:Platform=x64 --launch-profile "AzureCost.WinUI (Unpackaged)"`.
  It's **unpackaged + self-contained** → no Developer Mode, no MSIX deploy, no separate runtime.
  (To build packaged MSIX: set `<WindowsPackageType>MSIX</WindowsPackageType>`, enable Developer
  Mode, use the `(Package)` profile.)

## Decisions / gotchas (don't re-litigate)
- WinUI stays on **net10** (user's choice); Core + Console are net9.
- VM uses **field-based** `[ObservableProperty]` — the partial-property syntax broke the MVVM
  source generator on net9; `MVVMTK0045` is silenced via `<NoWarn>` in the csproj.
- Two crashes already fixed: (1) `CornerRadius` can't bind an `x:Double` StaticResource — use a
  literal; (2) subscription-select crashed from off-UI-thread `ObservableCollection` mutation —
  fixed by dispatcher marshalling. **Keep all WinUI collection/property writes on the UI thread.**

## Status (as of 2026-10-04)
Both apps build clean (0 warnings). Console verified end-to-end. WinUI auto-load verified
crash-free; **user to confirm the manual subscription-select fix next session.**

## Likely next steps
Confirm WinUI drill-down works for the user → then: charts/visualisation, CSV/JSON export,
response caching, group-by resource type, trend over time, unit tests for `ArmClient` parsing.
