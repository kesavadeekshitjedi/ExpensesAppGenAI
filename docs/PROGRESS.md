# Progress and Handoff Notes

**Last updated:** 2026-09-30
**Read this first** when picking the project back up. Then [SPEC.md](../SPEC.md) (what we're building and every decision) and [FAQ.md](FAQ.md) (how-tos and fixes already worked out).

---

## Where things stand

Build steps 1–4 are done and verified. Azure infrastructure is live in West US 2, deployed by the GitHub **Infrastructure** workflow through a deployment stack. Every push to `main` runs CI, then Deploy. The first automatic deploy (commit `9358348`) succeeded: the API image is `expenses-api:93583487…`, `/health` returns `Healthy`, and the web app serves the Home Expenses page.

Step 5 (database) is **written and committed locally, not yet pushed/verified in the pipeline.** EF Core + the first migration (`Households`) are in place; the Deploy workflow now creates the API identity's DB user and applies migrations before switching the API image. This needs a real deploy to confirm (see "Verify step 5" below).

---

## Next actions, in order

1. **Push and verify step 5 in the pipeline.** See "Verify step 5" below — the DB migration path has never actually run against Azure SQL, and a few assumptions (firewall, AAD auth from the runner) are only confirmed once Deploy runs.
2. **Build step 6: household members, sign-in (Microsoft/Google), invitations, Parent/Child roles.** Open Question 1 (children's ages / supervised accounts) must be answered first. See the step 6 notes below.
3. Continue down the build order.

### Verify step 5 (after pushing)

Push to `main`, let CI then Deploy run, and check:
- Deploy's **"Grant the API identity access to the database"** and **"Apply database migrations"** steps succeed. These use `azure/sql-action` with `Authentication=Active Directory Default`; it should auto-add/remove a temporary firewall rule for the runner. If AAD auth or the firewall handling fails, that's the first thing to debug (fallbacks noted in FAQ → Database).
- After deploy, the API can actually reach SQL at runtime (a future endpoint that queries the DB, or check App Insights for connection errors). The runtime user is created by `infra/sql/create-api-user.sql`.
- Confirm the `Households` table and `__EFMigrationsHistory` exist in `sqldb-expenses`.

---

## Build order checklist (Phase 1)

From SPEC.md "Build Order for Phase 1".

- [x] **1. Repo, solution, empty API with `/health`, React app running locally.** `ExpensesApp.slnx`, `src/api` (.NET 10), `src/web` (Vite + React + TypeScript), `tests/api.tests` (xUnit, 1 test). Verified locally: health test passes, web lints/builds, CORS works.
- [x] **2. CI workflow**: `.github/workflows/ci.yml` builds and tests the API, lints and builds the web app, and compiles the Bicep. Confirmed passing on GitHub.
- [x] **3. Azure resources and OIDC sign-in from GitHub.** `infra/bootstrap.ps1` (run once, done), `infra/main.bicep` + `infra/modules/*`, `.github/workflows/infra.yml` (what-if / apply / recreate). `apply` succeeded.
- [x] **4. CD workflow**: `.github/workflows/deploy.yml`. It runs after CI passes on `main`, builds and pushes the API image, updates the Container App, uploads the web app, and smoke-tests both. The first run succeeded.
- [x] **5. Database, core entities, and migrations in the pipeline** — *code committed; pipeline run not yet verified (see "Verify step 5").* EF Core 10 on the API, `ExpensesDbContext` + `Household` entity, initial migration, `dotnet-ef` as a local tool (`.config/dotnet-tools.json`), LocalDB for dev, and Deploy now grants the API identity's DB user and applies migrations before switching the image.
- [ ] **6. Household members, sign-in with Microsoft and Google, invitations, Parent/Child roles**
- [ ] 7. Payment methods and categories
- [ ] 8. Manual expense entry with line items, "for" tagging, value tags, and notes
- [ ] 9. Reports (basic)
- [ ] 10. Item database
- [ ] 11. Receipt capture, item matching, and "what is this item?" prompts
- [ ] 12. Budgets and alerts (in-app, then email)
- [ ] 13. Recurring bills
- [ ] 14. Item price comparison
- [ ] 15. Spending flags with color coding
- [ ] 16. Predictions

### Notes for step 5 (database) — how it was built

Decisions made (details in FAQ → Database):
- **EF Core 10** on the API. `ExpensesDbContext` + configurations in `src/api/Data`; entities in `src/api/Domain`; migrations in `src/api/Data/Migrations`. Only the `Household` entity exists so far (the root everything scopes to); the rest are added in their own steps.
- **`dotnet-ef` is a local tool** pinned in `.config/dotnet-tools.json`; `dotnet tool restore` before any `dotnet ef` command. An `ExpensesDbContextFactory` (design-time) lets `migrations`/`script` run without a database.
- **Local dev DB: SQL Server LocalDB** (`(localdb)\MSSQLLocalDB`, database `expenses-dev`), configured in `appsettings.Development.json`. Chosen because it ships with Visual Studio — no Docker. Switching to a container is just a connection-string change.
- **DbContext registration is guarded** (only when a connection string is present), so tests and connection-string-less environments still boot, matching the App Insights pattern in `Program.cs`.
- **The API identity's DB user is created `WITH SID`** (computed from its client ID) by `infra/sql/create-api-user.sql`, not `FROM EXTERNAL PROVIDER` — so the SQL server needs no Directory Readers Entra role, and `recreate` keeps working. Grants `db_datareader`/`db_datawriter` only (the API never runs migrations itself).
- **Deploy applies migrations** via `dotnet ef migrations script --idempotent` + `azure/sql-action`, after building the image and before switching it in. `azure/sql-action` handles AAD auth (OIDC session) and the temporary runner firewall rule, which resolves the old "can GitHub runners get through the SQL firewall?" question — pending the first real run to confirm.

### Notes for step 6 (sign-in), already decided

- **No client secrets** (SPEC decision #39). Microsoft sign-in uses MSAL with PKCE (register a single-page app that allows personal Microsoft accounts). Google uses Google Identity Services ID tokens. The API validates the ID token, checks the invitation, and starts its own session. Do **not** use `AddGoogle` / `AddMicrosoftAccount`.
- Sessions use ASP.NET Core Data Protection with keys in Blob Storage, encrypted with a Key Vault key through the managed identity. Infra changes needed then:
  - Add a Key Vault key, and give the API identity **Key Vault Crypto User** (replacing Key Vault Secrets User, which is no longer needed).
  - Add that role ID to `$assignableRoleIds` in `infra/bootstrap.ps1`, **re-run the bootstrap script**, then run `apply`. Verify role IDs with `az role definition list --name "<role>"` and never type them from memory (see FAQ).
- Open question 1 (children's ages / supervised accounts) must be answered before this step.

---

## Open questions and pending decisions

**From SPEC.md "Open Questions"** (still unanswered):
1. Children's ages, and whether any use supervised Google/Microsoft accounts. *Needed before step 6.*
2. Other "for" values besides members and Family (grandparents, pets, gifts)? *Needed before step 8.*
3. Do children receive alerts or emails? *Step 12.*
4. How are returns and refunds recorded? *Step 8.*
5. Single currency only? *Step 8.*
6. How long are receipt images kept? *Step 11 (Blob lifecycle rule).*
7. Attachments other than receipts (e.g., warranty PDFs)? *Step 11.*
8. How line item notes feed predictions: keyword rules or AI summarization? *Step 16.*
9. Windows client wanted? What tech? *Not scheduled.*

**Still-proposed items to confirm when their step comes up:**
- Deploy only the parts that changed (API vs. web). Currently both deploy every time.
- Branch protection on `main` so PRs need CI to pass. Check whether the GitHub plan allows it for a private repo.
- Web unit tests (e.g., Vitest) in CI. The spec says "run tests"; the web app has none yet.
- `recreate` mode has **no data backup step**. Add a database export (`.bacpac`) and receipt-image copy before real data exists.
- Email: the API identity has no role on Communication Services yet (step 12).
- The Static Web Apps deployment token is the one accepted exception to "no secrets" (fetched at deploy time, masked, never stored). The user was told about this; the alternative is Blob static website hosting.
- Cosmetic: role assignments show as "Unsupported" in what-if. This could be fixed by naming them from the identity's resource ID instead of its principal ID.
- Optional: add `.gitattributes` to stop Git's LF→CRLF warnings on Windows.

---

## Key facts

| Item | Value |
|---|---|
| GitHub repo | https://github.com/kesavadeekshitjedi/ExpensesAppGenAI (private), branch `main` |
| GitHub environment | `production`, with variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `SQL_ADMIN_GROUP_ID` |
| Azure subscription | `09b126a9-5a4c-4189-928e-8848ea663b26` (Pay-As-You-Go) |
| Tenant | `9388a572-b1c8-4cee-a0d7-68af646304e5` |
| Region | West US 2 |
| App resource group | `rg-expenses-prod` (deployment stack `expenses-app`) |
| Bootstrap resource group | `rg-expenses-bootstrap` (never delete) |
| GitHub deploy identity | `id-expenses-github`, client ID `6a7bddbb-8dee-42f5-a863-98f1b1cbc4e4`, trusts `repo:kesavadeekshitjedi/ExpensesAppGenAI:environment:production` |
| API identity | `id-expenses-api`, client ID `3ba2006b-f865-4fce-a848-3014e9c1b3d7` |
| SQL admin group | `Expenses SQL Admins`, object ID `6b2b5df3-fff6-4740-96d1-ebb5d9c046a4` (the user + deploy identity) |
| Custom role | `Expenses Soft-Delete Purger` (subscription scope, purge only) |
| API | https://ca-expenses-api.ashycliff-08d8073a.westus2.azurecontainerapps.io (Container App `ca-expenses-api`) |
| Web | https://ashy-desert-0e2b1851e.4.azurestaticapps.net (Static Web App `swa-expenses-prod`) |
| Container registry | `crexpenseseewun4ezq3r6k.azurecr.io`, repository `expenses-api`, tags = commit SHA |
| SQL | `sql-expenses-eewun4ezq3r6k.database.windows.net`, database `sqldb-expenses` (Basic), Entra-only sign-in |
| Budget ceiling | $50–100/month; expected ~$10–15/month now |
| Local ports | API http://localhost:5080, web http://localhost:5173 |

Live resource names and URLs: `az stack group show --name expenses-app --resource-group rg-expenses-prod --query outputs`.

---

## Lessons learned (details in FAQ.md)

- **Look up role IDs with `az role definition list`; never type them from memory.** A wrong AcrPull ID failed the first `apply`. Role IDs live in two places that must match: `infra/modules/*.bicep` and `$assignableRoleIds` in `infra/bootstrap.ps1`.
- On Windows, `az` is `az.cmd`, and `cmd.exe` mangles `( ) ! & |` in arguments that have no spaces. Pass complex values through files (`@file`).
- Personal (Hotmail) accounts: `az login` needs `az config set core.enable_broker_on_windows=false`.
- New managed identities take a minute or two to appear in Entra ID, so look them up by principal ID and retry.
- `az role definition update` needs the JSON shape that `list` returns, not the `create` shape.

---

## Session log

- **2026-09-15**: SPEC.md drafted from a Q&A session.
- **2026-09-17**: Spec reviewed. Confirmed Vite + TypeScript, Azure SQL + EF Core, Static Web Apps, a $50–100/month budget, and the repo URL.
- **2026-09-18**:
  - The user switched from "teaching mode" to "Claude writes the code, user reviews".
  - Built steps 1–3.
  - Chose Container Apps, SQL Basic, and West US 2.
  - Moved the deploy identity to its own resource group, with deployment stacks and a `recreate` mode.
  - Adopted the no-client-secrets design.
  - Started docs/FAQ.md.
  - Fixed a wrong AcrPull role ID; infra `apply` then succeeded.
  - Wrote the Deploy workflow; its first run succeeded (API healthy, web app live).
- **2026-09-30**:
  - Built step 5 (database): EF Core 10, `ExpensesDbContext` + `Household` entity + initial migration, `dotnet-ef` as a local tool, LocalDB for dev.
  - Added DB migration to the Deploy workflow (create API identity's DB user `WITH SID`, then apply an idempotent migration script via `azure/sql-action`, before switching the API image).
  - Documented it all in FAQ → Database. Committed locally; **not pushed** — needs a real deploy to verify the SQL steps.
