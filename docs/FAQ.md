# Project Q&A

Answers to questions that came up while building the app, so they can be looked up here instead of searched for again.
Newest questions are added to the relevant section. For how to run the app locally, see [README.md](../README.md).

## Contents

- [Azure CLI](#azure-cli)
- [Infrastructure (Bicep)](#infrastructure-bicep)
- [GitHub](#github)
- [Database](#database)
- [Sign-in and identity](#sign-in-and-identity)
- [Expense entry](#expense-entry)
- [Security and secrets](#security-and-secrets)
- [Visual Studio](#visual-studio)

---

## Azure CLI

### How do I sign in to Azure and select our subscription?

```powershell
az login
az account set --subscription 09b126a9-5a4c-4189-928e-8848ea663b26
az account show   # confirm the right subscription is active
```

### `az login` fails with "User '...@hotmail.com' does not exist in MSAL token cache"

This is a known problem between personal (Hotmail/Outlook.com) accounts and the Windows sign-in broker that `az` uses by default. Turn the broker off so `az login` uses a normal browser tab:

```powershell
az account clear
az config set core.enable_broker_on_windows=false
az login
az account set --subscription 09b126a9-5a4c-4189-928e-8848ea663b26
```

If it still fails, add `--tenant 9388a572-b1c8-4cee-a0d7-68af646304e5` to `az login`. (The tenant ID is also shown in the Azure portal under **Microsoft Entra ID > Overview**.)

### Why does an `az` command with parentheses in it fail with "Failed to load python executable"?

On Windows, `az` is a batch file (`az.cmd`), so `cmd.exe` can mangle characters like `( ) ! & |` in arguments that have no spaces. Either wrap the argument so it contains a space, or pass it from a file using the CLI's `@file` syntax (`--condition @condition.txt`). `infra/bootstrap.ps1` uses the file approach.

---

## Infrastructure (Bicep)

### How do I preview or deploy the infrastructure from my own machine instead of GitHub Actions?

Sign in first (see above). Run from the repo root in PowerShell:

```powershell
# The SQL admin group ID is read by infra/main.bicepparam
$env:SQL_ADMIN_GROUP_ID = '6b2b5df3-fff6-4740-96d1-ebb5d9c046a4'

# Keep the API image that is currently running; without this, apply resets the API to the placeholder image.
# (Returns nothing before the first deploy, which is fine.)
$env:API_IMAGE = az resource show -g rg-expenses-prod -n ca-expenses-api `
    --resource-type Microsoft.App/containerApps `
    --query "properties.template.containers[0].image" -o tsv 2>$null

# Preview (changes nothing)
az deployment group what-if `
    --resource-group rg-expenses-prod `
    --template-file infra/main.bicep `
    --parameters infra/main.bicepparam

# Apply (same as the workflow's apply mode)
az stack group create `
    --name expenses-app `
    --resource-group rg-expenses-prod `
    --template-file infra/main.bicep `
    --parameters infra/main.bicepparam `
    --action-on-unmanage deleteResources `
    --deny-settings-mode none `
    --yes
```

Prefer the GitHub workflow for real changes, so there's a record of what was deployed from which commit. Local runs are handy for quick checks. A local `apply` runs as you (the subscription owner), not as the deploy identity, so it won't catch permission problems the workflow would hit.

To only check that the templates compile: `az bicep build --file infra/main.bicep`.

### In what-if output, why do role assignments show as "Unsupported"?

Their names depend on the API identity's principal ID, which doesn't exist until the first deploy creates the identity. What-if can't evaluate that ahead of time. They are created normally on apply.

### The Infrastructure workflow failed. How do I see why without digging through the GitHub log?

Ask Azure for the stack's error and each module's status:

```powershell
az stack group show --name expenses-app --resource-group rg-expenses-prod --query "{state:provisioningState, error:error}" -o json
az deployment group list -g rg-expenses-prod --query "[].{name:name, state:properties.provisioningState}" -o table
```

The first shows the error message; the second shows which module (registry, sql, storage, ...) failed. Re-running `apply` after a fix is safe: resources that already succeeded are left as they are.

### `apply` failed with `RoleDefinitionDoesNotExist` for a role ID

A built-in role ID in the templates is wrong. (This happened once with AcrPull, whose correct ID is `7f951dda-4ed3-4680-a7ca-43fe172d538d`.) Look up the real ID rather than trusting memory:

```powershell
az role definition list --name AcrPull --query "[].{id:name, role:roleName}" -o table
```

Role IDs appear in **two** places, which must match: the module's `roleAssignments` in `infra/modules/*.bicep`, and `$assignableRoleIds` in `infra/bootstrap.ps1` (the list of roles the deploy identity may assign). After changing the bootstrap list, re-run `./infra/bootstrap.ps1` so the deploy identity's permission is updated; otherwise `apply` fails with an authorization error instead.

### Can we delete the resource group and redeploy if the architecture changes a lot?

Yes. The Infrastructure workflow has three modes:

| Mode | What it does |
|---|---|
| `what-if` | Preview only. Shows creates and changes, but not deletions. |
| `apply` | Creates or updates resources through a **deployment stack**. Anything removed from the templates is deleted, which covers most architecture changes without a full rebuild. |
| `recreate` | Deletes every app resource, purges soft-deleted Key Vault and Document Intelligence resources, then rebuilds. Requires typing `DELETE rg-expenses-prod`. **All data is lost**; add a database/receipt export-import step before using this with real data. |

Other options that were considered:
- **Complete mode** deployments (`--mode Complete`): delete anything in the resource group that isn't in the template. Older and blunter than stacks.
- **Deleting the resource group by hand**: works, but you must then purge the soft-deleted Key Vault and Document Intelligence resources yourself before `apply`, or the names are blocked.

The GitHub deploy identity lives in a separate resource group, `rg-expenses-bootstrap`, which is never deleted, so a rebuild can't remove GitHub's own access.

### What does `infra/bootstrap.ps1` set up, and when do I run it?

Run it once after `az login` (it's safe to re-run). It creates:
- `rg-expenses-bootstrap` with the deploy identity `id-expenses-github`, trusted only for GitHub Actions runs in the `production` environment of this repo (OIDC).
- `rg-expenses-prod`, where the deploy identity gets Contributor, plus the right to assign only the four roles the templates use.
- A custom role, **Expenses Soft-Delete Purger**, at subscription level. It only allows purging soft-deleted Key Vaults and Document Intelligence resources, which `recreate` needs.
- The Entra group **Expenses SQL Admins** (you and the deploy identity), which administers Azure SQL.

It prints the four values to add as GitHub environment variables.

### Why did the first bootstrap run fail with "Cannot find user or service principal in graph database"?

A newly created identity takes a minute or two to become visible in Entra ID. Any lookup *by identity* right after creation can fail. The script now matches on the principal ID and retries role assignments.

---

## GitHub

### Where do I create the `production` environment and its variables?

In the **repository's** settings, not your account settings:
https://github.com/kesavadeekshitjedi/ExpensesAppGenAI/settings/environments

Click **New environment**, name it `production`, and add these under **Environment variables** (none are secrets):

| Variable | Value |
|---|---|
| `AZURE_CLIENT_ID` | `6a7bddbb-8dee-42f5-a863-98f1b1cbc4e4` |
| `AZURE_TENANT_ID` | `9388a572-b1c8-4cee-a0d7-68af646304e5` |
| `AZURE_SUBSCRIPTION_ID` | `09b126a9-5a4c-4189-928e-8848ea663b26` |
| `SQL_ADMIN_GROUP_ID` | `6b2b5df3-fff6-4740-96d1-ebb5d9c046a4` |

If Environments isn't available (GitHub's Free plan may not offer it for private repos), switch to repository variables under **Settings > Secrets and variables > Actions > Variables**. You would also need to remove `environment: production` from the workflows and change the Azure federated credential subject to `repo:kesavadeekshitjedi/ExpensesAppGenAI:ref:refs/heads/main`.

### How do I run the Infrastructure workflow?

GitHub > **Actions** > **Infrastructure** > **Run workflow**, then pick the mode. Run `what-if` first, then `apply`. The workflow only runs code that has been pushed to `main`.

### How does a deploy happen?

Automatically: push (or merge a PR) to `main` → **CI** runs → if CI passes, **Deploy** starts and deploys exactly that commit. Deploy builds the API image, points the Container App at it, checks `/health`, then builds and uploads the web app and checks the page loads. If CI fails, nothing is deployed.

To deploy by hand (for example after running `infra` `recreate`, which leaves the API on the placeholder image): **Actions > Deploy > Run workflow**.

### How do I see why a workflow (CI, Deploy, Infrastructure) failed from the terminal?

Use the **GitHub CLI** (`gh`; install with `winget install GitHub.cli`, then `gh auth login` once). From the repo root:

```powershell
gh run list --limit 5                     # recent runs and their status
gh run view <run-id>                       # per-job / per-step summary
gh run view <run-id> --log-failed          # just the logs of the steps that failed
gh run watch <run-id>                      # follow a run live
```

`gh` is required for working with Actions from the terminal (this repo's CI, Deploy, and Infrastructure workflows). If `gh` is freshly installed, open a new terminal so it is on `PATH`, or call it by full path (`& "C:\Program Files\GitHub CLI\gh.exe"`).

### Why didn't Deploy run after I pushed?

Deploy only starts when CI **succeeds** for a push to `main`. Check the CI run first; fix it and push again. Pull request CI runs never deploy.

### How do I roll back a bad deploy?

- **Normal way:** revert the commit on `main` (`git revert <sha>` and push). CI and Deploy run again and deploy the previous code.
- **Fastest way for the API:** every deploy creates a new Container App revision. List them and send traffic back to the previous one:
  ```powershell
  az containerapp revision list -n ca-expenses-api -g rg-expenses-prod --query "[].{name:name, image:properties.template.containers[0].image, created:properties.createdTime}" -o table
  az containerapp revision activate -n ca-expenses-api -g rg-expenses-prod --revision <revision-name>
  ```
  The next normal deploy replaces it again. (The web app has no revisions on the free tier; roll it back by reverting.)

### Which API version is running right now?

The image tag is the commit SHA it was built from:

```powershell
az containerapp show -n ca-expenses-api -g rg-expenses-prod --query "properties.template.containers[0].image" -o tsv
```

### Does the deploy use any secret?

One, unavoidably: Static Web Apps only accepts uploads with its **deployment token**. The Deploy workflow fetches it at run time through the OIDC sign-in, masks it in the log, and never stores it in GitHub. Everything else (registry push, Container App update) uses the OIDC sign-in directly.

---

## Database

The API uses **Entity Framework Core** against **Azure SQL** in production and **SQL Server LocalDB** for local development. Entities live in `src/api/Domain`, the `ExpensesDbContext` and its configurations in `src/api/Data`, and migrations in `src/api/Data/Migrations`.

### How do I set up the local development database?

LocalDB ships with Visual Studio (and the "Data storage and processing" workload), so if you have Visual Studio you already have it. The development connection string is in `src/api/appsettings.Development.json` and points at `(localdb)\MSSQLLocalDB`, database `expenses-dev`.

Create the local database and apply all migrations:

```powershell
dotnet tool restore                 # first time only: installs dotnet-ef from .config/dotnet-tools.json
dotnet ef database update --project src/api
```

Then `dotnet run --project src/api` connects to it. To start over, `dotnet ef database drop --project src/api` and update again. (If you'd rather use a SQL Server container than LocalDB, change the `ConnectionStrings:Expenses` value in `appsettings.Development.json`.)

### How do I add a new entity or change the schema?

1. Add or edit the class in `src/api/Domain` and its configuration in `src/api/Data/Configurations`.
2. Create a migration (pick a descriptive name):
   ```powershell
   dotnet ef migrations add AddPaymentMethods --project src/api --output-dir Data/Migrations
   ```
3. Review the generated `Up`/`Down` in `src/api/Data/Migrations`, then apply it locally with `dotnet ef database update --project src/api`.
4. Commit the migration files. The deploy workflow applies them to production automatically.

Migrations must be **backward-compatible with the running API** (the old API keeps serving while the new schema goes on), because migrations are applied before the new API image is switched in.

### How do migrations reach the production database?

The **Deploy** workflow does it, after building the API image and before switching the Container App to it (so a failed migration never ships a new API):

1. `dotnet ef migrations script --idempotent` turns the committed migrations into one re-runnable SQL script. This needs no database connection; the design-time connection string in `ExpensesDbContextFactory` is only used to build the model.
2. `infra/sql/create-api-user.sql` creates the API identity's database user and grants it `db_datareader`/`db_datawriter` (idempotent).
3. The script from step 1 is applied.

Steps 2 and 3 run with `sqlcmd` (`--authentication-method ActiveDirectoryDefault`), authenticating with the workflow's OIDC session — no SQL password. GitHub-hosted runners run on Azure and reach the server through the SQL server's "Allow Azure services" (`0.0.0.0`) firewall rule, so no firewall change is needed. Each script is retried, because Azure SQL can return a transient error on the first connection to an idle database (see below).

### The deploy failed at "Apply database changes" with "Database '...' is not currently available"

That is Azure SQL error **40613**, a transient error that commonly hits the *first* connection to an idle Basic-tier database while the platform brings it online. Auth and networking are fine when you see it (the error comes from the database, not the login or firewall). The deploy retries each SQL script up to 10 times (15s apart), which normally rides it out. If it still fails after retries, just re-run the Deploy job (**Actions > Deploy**, or `gh run rerun <run-id>`); it is safe because both scripts are idempotent.

An earlier version used `azure/sql-action`, which does not retry, so a single 40613 failed the whole deploy. It was replaced with the `sqlcmd` retry loop for that reason.

### How does the API sign in to SQL with no password?

Its connection string (set on the Container App by `infra/main.bicep`) uses `Authentication=Active Directory Managed Identity` with the `id-expenses-api` identity's client ID. The matching database user is created during deploy by `infra/sql/create-api-user.sql`.

That script creates the user **`WITH SID`**, computing the SID from the identity's client ID (`0x` + `Guid.ToByteArray()` in hex), rather than `CREATE USER [id-expenses-api] FROM EXTERNAL PROVIDER`. `FROM EXTERNAL PROVIDER` would require the SQL server to have a managed identity with the **Directory Readers** Entra role so it can look the name up; `WITH SID` needs no such Entra permission and keeps working after `recreate` regenerates the identity, because the deploy recomputes the SID each run.

### A migration or user-grant step failed with "Login failed" or "principal could not be resolved"

- **Login failed for the API identity at runtime** usually means `create-api-user.sql` did not run or the SID didn't match. Confirm the deploy's "Grant the API identity access to the database" step succeeded, and that `API_IDENTITY_CLIENT_ID` in the deploy log matches the current `id-expenses-api` client ID (`az stack group show --name expenses-app --resource-group rg-expenses-prod --query outputs.apiIdentityClientId.value`).
- **"Principal 'id-expenses-api' could not be resolved"** only happens if you switch the script to `FROM EXTERNAL PROVIDER`; the `WITH SID` approach avoids it.

---

## Sign-in and identity

### How was the Microsoft sign-in app registration created?

With the Azure CLI, as a **single-page app (SPA)** registration with **no secret** (sign-in uses MSAL with PKCE — SPEC decision #39). After `az login`:

```powershell
# Create the app registration (personal + org accounts).
$app = az ad app create --display-name "Home Expenses web" `
    --sign-in-audience AzureADandPersonalMicrosoftAccount -o json | ConvertFrom-Json
$app.appId   # Application (client) ID
$app.id      # object ID (needed for the next step)

# Add the SPA redirect URIs. The az CLI has no direct flag for SPA redirect URIs, so PATCH the
# application's `spa` property through Microsoft Graph. (Put the JSON in a file to avoid Windows
# quoting issues — see the Azure CLI section.)
# body.json: {"spa":{"redirectUris":["http://localhost:5173","https://ashy-desert-0e2b1851e.4.azurestaticapps.net"]}}
az rest --method PATCH `
    --uri "https://graph.microsoft.com/v1.0/applications/$($app.id)" `
    --headers "Content-Type=application/json" `
    --body "@body.json"

# Verify
az ad app show --id $app.appId --query "{audience:signInAudience, spaRedirects:spa.redirectUris}" -o json
```

Result (all **non-secret**, safe to commit and put in config):

| | |
|---|---|
| Application (client) ID | `7c5331e4-7ad2-4880-bbec-597b6338036f` |
| Object ID | `c1106fbb-0c56-4951-83cd-1f140fd64238` |
| Sign-in audience | personal + any org account |
| SPA redirect URIs | `http://localhost:5173`, `https://ashy-desert-0e2b1851e.4.azurestaticapps.net` |
| Authority | `https://login.microsoftonline.com/common` |

No client secret or certificate was created, and none should be — the browser gets an ID token via PKCE and the API validates it. The app registration lives in Entra ID, not in `rg-expenses-prod`, so `infra` `recreate` does not delete it. To add another redirect URI later (e.g., a new environment), re-run the `az rest` PATCH with the full list.

### How does signing in actually work, end to end?

1. The web app uses **MSAL** (`@azure/msal-browser`) to sign the user in with Microsoft (PKCE, no secret). The browser receives a signed **ID token**.
2. The web app `POST`s that token to the API's **`/auth/session`** (with `credentials: include`).
3. The API validates the token (signature against Microsoft's keys, audience = our client ID, issuer = a real Microsoft issuer) and then:
   - existing member → signs in;
   - valid invitation code in the request → creates the member with the invited role and marks the invitation accepted;
   - no household exists yet (first ever sign-in) → creates the household and makes this user a **Parent** (SPEC #45);
   - otherwise → `403` (an invitation is required).
4. On success the API issues a **session cookie** (ASP.NET Core cookie auth). Its Data Protection keys live in the `dataprotection` blob container, encrypted by the Key Vault `dataprotection` key, both via the managed identity. Later requests send the cookie; `GET /auth/me` returns the current member; `POST /auth/logout` clears it.

Children are **view-only**: the `Parent` authorization policy guards every write (adding members, creating/revoking invitations). Invitations are **shareable links** (`https://<web>/?invite=<code>`) the parent sends themselves; there is no invitation email in phase 1.

### How do I run and test sign-in locally?

Copy `src/web/.env.example` to `src/web/.env.local` (both values are non-secret), create the local database (see the Database section), then run the API and web as usual. `http://localhost:5173` is already a redirect URI on the app registration, so Microsoft sign-in works locally. The API reads the client ID from `appsettings.json` (`Auth:Microsoft:ClientId`); locally, sessions use the default Data Protection key ring (Azure Blob/Key Vault are only wired when their config is present), so no Azure access is needed just to sign in.

---

## Expense entry

Steps 7 and 8 added payment methods, categories, and manual expense entry. The web app's dashboard has three tabs: **Expenses** (enter + view), **Settings** (categories + payment methods), and **Household** (members + invitations). Only **parents** can enter or change anything; children are view-only.

### How do I enter an expense in the web app?

Sign in, go to the **Expenses** tab, and fill in the form:

1. **Merchant** (e.g. `Costco`), **date** (defaults to today), and **payment method** (from the ones you created in Settings).
2. A **default "for"** (Family, or a specific member) that pre-fills each new line — you can change it per line.
3. One or more **items**, each with a description, category, who it was "for", quantity, unit price, an optional explicit amount (otherwise quantity × unit price), an optional **value tag**, and an optional note.
4. **Save expense.** The total shown is the sum of the line amounts; the API recomputes it on save so it's authoritative.

The saved expense appears in **Recent expenses** below the form, with each line's figured-out short form shown in brackets, e.g. `Kirkland Organic Eggs … [KIRKL ORGAN EGGS]`.

A first-time household: the **Settings** tab starts with the default category list already seeded, but **no payment methods** — add at least one (e.g. "Discover card") before the entry form will let you save.

### How does the app "figure out the short form"? (the `ShortForm` generator)

When you type an item's **full name** during manual entry, the app derives a receipt-style **short form** (the `ItemReceiptDescription`) so future receipt scans (step 11) can match the item. It is deterministic, with **no AI** (SPEC #16 and #48): upper-case the name, turn punctuation into spaces, abbreviate any word longer than 5 letters to its first 5, and keep whole words up to 24 characters. For example:

- `Kirkland Signature Organic Eggs, 24 ct` → `KIRKL SIGNA ORGAN EGGS`
- `Milk` → `MILK`

The logic lives in `src/api/Domain/ShortForm.cs`. You can override it by sending an explicit `shortForm` on a line; if two different items would get the same short form at one merchant, the later one is suffixed (`APPLE`, `APPLE 2`, …), because `(merchant, printed description)` is unique. Each manual line also resolves-or-creates an `Item` (by full name, per household), so the item database fills up as you enter — this is the "lite" start of step 10 (full name → short form only; item merge/pictures come later).

### Where do categories come from, and how do I manage them?

New households are seeded with a default list — Groceries, Dining out, Utilities, Household, Transportation, Entertainment, Health, Kids, Subscriptions (SPEC #47, `src/api/Domain/DefaultCategories.cs`). The household created before step 7 is **backfilled** the first time a parent opens Settings / calls `GET /categories` on an empty list (idempotent). Parents can add new categories and **archive/restore** them (archived categories stay on past line items but drop out of the entry-form picker). Renames are supported by the API (`PATCH /categories/{id}`) though the web app currently only adds and archives.

### Why can't I store a card or account number on a payment method?

By design (SPEC #12 / feature 1): a payment method is a **label and a type only** (`CreditCard`, `BankAccount`, `Cash`, `Other`) — e.g. "Discover card", credit card. There are deliberately no fields for card numbers, account numbers, or balances.

### What are the new endpoints?

All require a session cookie; writes require the `Parent` policy.

| Method & path | Who | Purpose |
|---|---|---|
| `GET /categories` | any member | List categories (a parent on an empty list triggers the default backfill) |
| `POST /categories`, `PATCH /categories/{id}` | Parent | Add; rename and/or archive |
| `GET /payment-methods` | any member | List payment methods |
| `POST /payment-methods`, `PATCH /payment-methods/{id}` | Parent | Add; rename, retype, archive |
| `GET /expenses`, `GET /expenses/{id}` | any member | List (newest first, capped at 200) / read one, with readable names |
| `POST /expenses` | Parent | Create an expense with line items; creates the merchant, items, and new value tags, and sums the total |
| `GET /reports/summary?from=&to=` | any member | Period spending total + breakdowns (see Reports below) |

### How do reports work? (the Reports tab)

The **Reports** tab calls `GET /reports/summary`. With no dates it covers the **current calendar month**; otherwise pass `from` and `to` (inclusive, `YYYY-MM-DD`). It returns the **total** for the period and six breakdowns — **by category, by who it was "for"** (each member and Family), **by payment method, by merchant, by item, and by value tag** — each sorted by amount, with each row's share of the total shown as a percentage. Everyone in the household can view reports, including view-only children (SPEC feature 11).

The endpoint loads the expenses in range and groups them in memory (`src/api/Endpoints/ReportEndpoints.cs`); phase-1 household volumes are small, so this stays simple and works the same on LocalDB and Azure SQL. There's **no new database table or migration** for reports. Not yet included (later steps): **budget vs. actual** (needs budgets, step 12), flag colors (step 15), and note search (part of a fuller reports pass).

### How do I run and test expense entry locally?

Make sure the local database is up to date (the new migrations add the tables):

```powershell
dotnet ef database update --project src/api    # applies AddPaymentMethodsAndCategories + AddExpenseEntry
dotnet run --project src/api                    # API on http://localhost:5080
cd src/web; npm run dev                         # web on http://localhost:5173
```

Sign in with Microsoft (works locally — see Sign-in and identity), add a payment method in **Settings**, then enter an expense in **Expenses**. The API tests cover the endpoints without a browser or real sign-in: `ExpenseApiTests` drives the real pipeline through a `WebApplicationFactory` (`TestApiFactory`) that uses an in-memory database and a stub parent-auth scheme, running in a **"Testing"** environment so `Program` skips the SQL Server provider. Run everything with `dotnet test ExpensesApp.slnx`.

---

## Security and secrets

### How do we avoid ever needing client secrets?

Nothing in the design uses a secret, key, or password:

| Connection | How it signs in |
|---|---|
| GitHub Actions → Azure | OpenID Connect (federated credential on `id-expenses-github`) |
| API → SQL, Blob Storage, Key Vault, Document Intelligence, Email | The API's managed identity (`id-expenses-api`); key/password sign-in is disabled where the service allows it |
| Container Apps → logs | Azure Monitor diagnostic setting (no Log Analytics shared key) |
| Family sign-in (Microsoft, Google) | The browser gets a signed ID token directly from the provider (MSAL with PKCE; Google Identity Services). The API only verifies the token against the provider's public keys |
| API sessions | ASP.NET Core Data Protection keys stored in Blob Storage and encrypted with a Key Vault key, via the managed identity |

ASP.NET Core's server-side `AddGoogle` / `AddMicrosoftAccount` handlers are deliberately **not** used, because they require a client secret. Any new integration should first look for a managed-identity or public-client option.

### Is the SQL server exposed to the internet?

The firewall allows traffic from Azure services, because Container Apps on the consumption plan has no fixed outbound IP. What protects the server is that only Entra identities can sign in: there are no SQL usernames or passwords at all.

---

## Visual Studio

### Solution Explorer only shows the API and tests. Where are the web app, Bicep, and workflows?

Visual Studio's solution view only lists .NET projects. Click **Switch between solutions and available views** at the top of Solution Explorer (the folder icon with the VS logo) and choose **Folder View** to see every file. Or open the folder in VS Code.
