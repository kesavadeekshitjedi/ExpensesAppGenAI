# Home Expenses

Family expense tracker. See [SPEC.md](SPEC.md) for the full project spec.

## Layout

| Path | What |
|---|---|
| `src/api` | ASP.NET Core Web API (.NET 10) |
| `src/web` | React web app (Vite + TypeScript) |
| `tests/api.tests` | API tests (xUnit) |
| `infra` | Bicep templates and the one-time bootstrap script |
| `.github/workflows` | CI and infrastructure workflows |

## Run locally

Prerequisites: .NET SDK 10, Node.js 24.

```powershell
# Terminal 1 - API on http://localhost:5080
dotnet run --project src/api

# Terminal 2 - web app on http://localhost:5173
cd src/web
npm install
npm run dev
```

The home page shows **API status: healthy** when both are running.

Tests: `dotnet test ExpensesApp.slnx`

## Azure setup (one time)

1. `az login`, then run the bootstrap script. It creates the resource group, the identity GitHub Actions signs in as, and the SQL admin group:
   ```powershell
   ./infra/bootstrap.ps1 -SubscriptionId <subscription-id>
   ```
2. In GitHub, go to **Settings > Environments**, create an environment named `production`, and add the four **variables** the script prints (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `SQL_ADMIN_GROUP_ID`). None of them are secrets.
3. In GitHub, go to **Actions > Infrastructure > Run workflow**. Run it with `what-if` first to preview changes, then run it again with `apply`.

The API starts on a placeholder image until the deploy workflow (build step 4) pushes the real one.

### Resources and rough monthly cost

| Resource | Tier | ~Cost/month |
|---|---|---|
| Container Apps (API) | Consumption, scales to zero | ~$0 (free monthly grant) |
| Container Registry | Basic | ~$5 |
| Azure SQL Database | Basic, 5 DTU | ~$5 |
| Static Web Apps | Free | $0 |
| Storage (receipts, item pictures) | Standard LRS | <$1 |
| Document Intelligence | F0 (500 pages/month) | $0 |
| Communication Services Email | Azure-managed domain | pennies per email |
| Key Vault, Log Analytics (0.5 GB/day cap), App Insights | | ~$1–3 |

Check current Azure pricing before applying; prices change.

### Notes

- No passwords or keys are stored anywhere. GitHub signs in with OIDC; the API uses its managed identity for SQL, Storage, Key Vault, and Document Intelligence.
- Key Vault uses 7-day soft delete. If you delete the resource group and rebuild within 7 days, purge the old vault first (`az keyvault purge --name <name>`).
- Only one free (F0) Document Intelligence resource is allowed per subscription. If you already have one, set `documentIntelligenceSku` to `S0` in `infra/main.bicep`.
