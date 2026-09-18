# Project Q&A

Answers to questions that came up while building the app, so they can be looked up here instead of searched for again.
Newest questions are added to the relevant section. For how to run the app locally, see [README.md](../README.md).

## Contents

- [Azure CLI](#azure-cli)
- [Infrastructure (Bicep)](#infrastructure-bicep)
- [GitHub](#github)
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
