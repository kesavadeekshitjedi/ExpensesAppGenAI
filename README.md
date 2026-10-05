# Azure Cost Explorer

A C# console app that shows your Azure spend and lets you **drill into resource groups** to
see which resource is costing how much. The architecture is deliberately split so a
**WinUI 3** (or any other) front-end can be plugged in without touching the Azure logic.

```
azurecost/
├─ AzureCost.slnx
└─ src/
   ├─ AzureCost.Core/        ← all Azure logic + models + DI (no UI dependency)
   └─ AzureCost.Console/     ← Spectre.Console front-end (the pluggable part)
```

## How the layering enables a WinUI 3 app

Everything a front-end needs lives behind one interface, `IAzureCostService`, which returns
plain data records (`SubscriptionCostSummary`, `ResourceGroupCost`, `ResourceCost`). The Core
library references **no** console/UI packages.

- **Console today:** `Program.cs` builds a host, calls `services.AddAzureCost()`, and the
  `ConsoleCostExplorer` injects `IAzureCostService`.
- **WinUI 3 tomorrow:** the WinUI `App.xaml.cs` builds a host the same way, calls the same
  `services.AddAzureCost()`, and a view model injects the same `IAzureCostService` — binding
  the returned records to a `TreeView`/`DataGrid` instead of printing them. No change to Core.

Because `AddAzureCost(options => …)` accepts a custom `TokenCredential`, a WinUI app can swap
in an `InteractiveBrowserCredential` (nice sign-in popup) while the console keeps using
`DefaultAzureCredential` — again, zero changes to the shared logic.

## Prerequisites

- .NET 9 SDK (installed) — also builds under .NET 10.
- **Cost Management Reader** (or Reader/Contributor/Owner) on the subscription.
- Be signed in with any identity `DefaultAzureCredential` understands. The simplest is the
  Azure CLI:

  ```
  az login
  ```

  It also honours Visual Studio sign-in, `AZURE_*` env-var service principals, and managed
  identity when deployed.

## Run it

```
dotnet run --project src/AzureCost.Console
```

Flow:
1. Pick a subscription (auto-selected if you only have one).
2. See the total for the period plus a per-resource-group table with a share bar.
3. Choose **Drill into <resource group>** to see cost per individual resource.
4. Switch the period (Month to date / Last month / Last 7 / Last 30 days) or subscription
   any time.

> First run is slow by a few seconds while the Cost Management backend warms up; later
> queries are fast.

## Configuration (optional)

Override defaults via `AzureCost:*` config or environment variables:

| Setting        | Env var                   | Default                   | Notes                                             |
|----------------|---------------------------|---------------------------|---------------------------------------------------|
| `TenantId`     | `AzureCost__TenantId`     | (credential default)      | Pin sign-in to a specific tenant.                 |
| `CostMetric`   | `AzureCost__CostMetric`   | `Cost`                    | Use `PreTaxCost` for some EA offers.              |
| `ArmBaseUri`   | `AzureCost__ArmBaseUri`   | `https://management.azure.com` | Change for sovereign clouds.                 |
| `CostApiVersion` | `AzureCost__CostApiVersion` | `2023-11-01`           | Cost Management query API version.                |

Example:

```
$env:AzureCost__CostMetric = "PreTaxCost"
dotnet run --project src/AzureCost.Console
```

## How costs are fetched

`AzureCost.Core` calls the **Cost Management Query API**
(`POST .../providers/Microsoft.CostManagement/query`) with `ActualCost` and a `Sum` of the
cost metric:

- **Subscription → resource groups:** query at subscription scope, grouped by
  `ResourceGroupName`.
- **Resource group → resources:** query at the resource-group scope, grouped by `ResourceId`
  (the friendly name and resource type are parsed from the id).

Column positions are looked up by name, so the parser tolerates ordering changes across API
versions.

## The WinUI 3 app (`src/AzureCost.WinUI`)

A full rich desktop front-end over the **same** `AzureCost.Core` library — proof the layering
works. It has a Mica-backdrop window with:

- Subscription + period pickers and a Refresh button in the toolbar.
- A big "Total spend" summary card.
- A master-detail view: **resource groups** on the left (each with a cost + share bar); click
  one to drill into its **individual resources** on the right.
- Busy rings and an error `InfoBar`.

The only difference from the console at the wiring level is the credential — the WinUI app
uses an interactive sign-in fallback:

```csharp
// App.xaml.cs
services.AddAzureCost(options =>
{
    options.Credential = new ChainedTokenCredential(
        new AzureCliCredential(),
        new VisualStudioCredential(),
        new InteractiveBrowserCredential(/* browser popup fallback */));
});
services.AddTransient<MainViewModel>();   // MainViewModel injects IAzureCostService
```

### Run it

- **Target framework:** `net10.0-windows` — needs Visual Studio 2026 (18.x) or the .NET 10 SDK.
- **Platform:** build as **x64** (or ARM64), not Any CPU.
- Configured to run **unpackaged + self-contained**, so it needs neither Developer Mode nor an
  MSIX deploy nor a separate Windows App SDK runtime install.

From Visual Studio 2026:
1. If prompted after these edits, **reload the project**.
2. Set the **Solution Platform** to `x64`.
3. In the Start-button dropdown choose the **`AzureCost.WinUI (Unpackaged)`** launch profile
   (the `(Package)` profile is what triggers the "enable Deploy / Developer Mode" dialog).
4. Set `AzureCost.WinUI` as the startup project and press **F5**.

From the CLI:

```
dotnet run --project src/AzureCost.WinUI -p:Platform=x64 --launch-profile "AzureCost.WinUI (Unpackaged)"
```

> To build the packaged **MSIX** instead (Store-style install, app identity), set
> `<WindowsPackageType>MSIX</WindowsPackageType>` in the csproj, enable **Developer Mode**
> (Settings → System → For developers), and use the `(Package)` profile.
