# Home Expenses

Family expense tracker. See [SPEC.md](SPEC.md) for the full project spec and [docs/FAQ.md](docs/FAQ.md) for answers to questions that came up during the build.

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

## Production

| What | Address |
|---|---|
| Web app | https://ashy-desert-0e2b1851e.4.azurestaticapps.net |
| API | https://ca-expenses-api.ashycliff-08d8073a.westus2.azurecontainerapps.io |

Both run placeholders until the deploy workflow (build step 4) exists. Current values: `az stack group show --name expenses-app --resource-group rg-expenses-prod --query outputs`.

## Azure setup (one time)

1. `az login`, then run the bootstrap script:
   ```powershell
   ./infra/bootstrap.ps1 -SubscriptionId <subscription-id>
   ```
   It creates two resource groups:
   - `rg-expenses-bootstrap` holds the identity GitHub Actions signs in as. It is never deleted, so the app can be torn down and rebuilt without losing deploy access.
   - `rg-expenses-prod` holds the app. The deploy identity can manage everything in it, but can only assign the four roles the templates use.

   It also creates the SQL admin group (you and the deploy identity), plus a small custom role that lets the deploy identity purge deleted Key Vaults and Document Intelligence resources, which `recreate` needs.
2. In GitHub, go to **Settings > Environments**, create an environment named `production`, and add the four **variables** the script prints (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `SQL_ADMIN_GROUP_ID`). None of them are secrets.
3. In GitHub, go to **Actions > Infrastructure > Run workflow**. Run it with `what-if` first to preview changes, then run it again with `apply`.

### Infrastructure workflow modes

| Mode | What it does |
|---|---|
| `what-if` | Preview only. Shows creates and changes, but not deletions. |
| `apply` | Creates or updates resources through a deployment stack. Resources removed from the templates are deleted. |
| `recreate` | Deletes every app resource, purges soft-deleted ones, then rebuilds. **All data (database, receipt images) is lost.** Requires typing `DELETE rg-expenses-prod`. After it runs, the API is on the placeholder image until the next deploy. |

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
- Key Vault and Document Intelligence keep deleted resources for a while and block reusing their names. `recreate` purges them automatically; if you delete the resource group by hand instead, purge them yourself before running `apply`.
- `recreate` does not back up data yet. Add a database export/import step before using it once there is real data.
- Only one free (F0) Document Intelligence resource is allowed per subscription. If you already have one, set `documentIntelligenceSku` to `S0` in `infra/main.bicep`.
