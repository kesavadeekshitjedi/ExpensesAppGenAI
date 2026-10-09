# Progress and Handoff Notes

**Last updated:** 2026-10-08
**Read this first** when picking the project back up. Then [SPEC.md](../SPEC.md) (what we're building and every decision) and [FAQ.md](FAQ.md) (how-tos and fixes already worked out).

---

## Where things stand

Build steps 1–4 are done and verified. Azure infrastructure is live in West US 2, deployed by the GitHub **Infrastructure** workflow through a deployment stack. Every push to `main` runs CI, then Deploy. The first automatic deploy (commit `9358348`) succeeded: the API image is `expenses-api:93583487…`, `/health` returns `Healthy`, and the web app serves the Home Expenses page.

Step 5 (database) is **done and verified in production (2026-09-30).** Deploy run `36746336438` created the API identity's SQL user, applied both migrations (`Households`, `Members`/`Invitations`) recorded in `__EFMigrationsHistory`, switched the Container App to the new image, and `/health` returns `Healthy`. Confirmed along the way: OIDC/AAD auth works from the runner and GitHub-hosted runners pass the SQL "Allow Azure services" firewall rule.

Step 6 (members, sign-in, invitations, roles) — **Microsoft sign-in is done, deployed, and verified end to end in the browser (2026-09-30).** Signing in with Microsoft creates your household and makes you Parent; the API uses the database at runtime (members/invitations). Runtime managed-identity SQL access is therefore proven. **Google sign-in is the one remaining piece of step 6.**

Step 7 (payment methods & categories) — **built and tested locally (2026-10-08); not yet deployed.** Entities `PaymentMethod` (label + type only, SPEC #12) and `Category`, household-scoped, Parent-only writes. New households are seeded with the default category list (SPEC feature 5); a parent reading an empty `/categories` backfills it, so the existing household gets defaults too. Web Settings tab manages both.

Step 8 (manual expense entry) — **built and tested locally (2026-10-08); not yet deployed.** `Expense` + `LineItem` with per-line category, "for" (a member or Family), value tag, and notes; `Merchant` and `ValueTag` created on demand. A **lite item database** (`Item` + `ItemReceiptDescription`, part of step 10) is populated as you enter: you type the full item name and the app **figures out the receipt "short form"** for you (deterministic `ShortForm` generator, no AI — SPEC #16). `POST /expenses` sums the line amounts into the total. Web Expenses tab has the entry form and a recent-expenses list that shows each line's short form.

Step 9 (reports, basic) — **built and tested locally (2026-10-08).** `GET /reports/summary?from=&to=` (defaults to the current month) returns the period total plus breakdowns by category, who it was "for", payment method, merchant, item, and value tag; household-scoped and readable by children (SPEC feature 11). No schema change. Web **Reports** tab with a date range and breakdown tables.

**Local verification (2026-10-08):** API + web both build and lint clean; 25 API tests pass (unit + end-to-end via `WebApplicationFactory`); all four EF migrations apply cleanly to LocalDB. **Pushed on 2026-10-08** so CI → Deploy would run (steps 7–9). **Not yet exercised in the browser against the real DB** — confirm after the deploy completes.

---

## Next actions, in order

**Pick up here next session.**

1. **Confirm the steps 7–9 deploy and verify in the browser.** Steps 7–9 were pushed on 2026-10-08. Check CI → Deploy succeeded (`gh run list`), confirm the two migrations (`AddPaymentMethodsAndCategories`, `AddExpenseEntry`) applied to production, then sign in and: add a payment method, enter an expense, open the Reports tab (mirrors how step 6 was confirmed). If the deploy failed, see FAQ → GitHub for how to read the logs.
2. **Google sign-in (unfinished part of step 6).** Google Identity Services ID tokens. Add a `GoogleIdentityValidator : IExternalIdentityValidator` (issuer `https://accounts.google.com`, audience = a Google OAuth **Web** client ID, signature via Google's JWKS). Create a Google OAuth client ID (Google Cloud Console; no secret for the GIS ID-token flow). Add a "Sign in with Google" button that POSTs the ID token to `/auth/session` with `provider: "Google"`. Provisioning/sessions/invitations/roles are already provider-agnostic — only validation + a button are new.
3. **Finish the item database (step 10)** or move to **receipt capture (step 11)** — pick with the user. The lite item DB (full name → short form) already exists.
4. Then the rest of the build order (budgets, recurring bills, price comparison, flags, predictions).

### Loose ends to tidy (non-blocking)

- Doc-only commits still trigger a full redeploy (CI→Deploy on every push to `main`). The "deploy only changed parts" item is still open (see pending decisions).
- Google sign-in (action 2 above) is the only unfinished part of step 6.
- Expense **edit/delete** endpoints are not built yet (only create/list/get). Add when editing is needed.
- Only the **lite** item database exists (full name → short form). Item merge, pictures, and editing (the rest of step 10) are still to come.

---

## Build order checklist (Phase 1)

From SPEC.md "Build Order for Phase 1".

- [x] **1. Repo, solution, empty API with `/health`, React app running locally.** `ExpensesApp.slnx`, `src/api` (.NET 10), `src/web` (Vite + React + TypeScript), `tests/api.tests` (xUnit, 1 test). Verified locally: health test passes, web lints/builds, CORS works.
- [x] **2. CI workflow**: `.github/workflows/ci.yml` builds and tests the API, lints and builds the web app, and compiles the Bicep. Confirmed passing on GitHub.
- [x] **3. Azure resources and OIDC sign-in from GitHub.** `infra/bootstrap.ps1` (run once, done), `infra/main.bicep` + `infra/modules/*`, `.github/workflows/infra.yml` (what-if / apply / recreate). `apply` succeeded.
- [x] **4. CD workflow**: `.github/workflows/deploy.yml`. It runs after CI passes on `main`, builds and pushes the API image, updates the Container App, uploads the web app, and smoke-tests both. The first run succeeded.
- [x] **5. Database, core entities, and migrations in the pipeline** — *code committed; pipeline run not yet verified (see "Verify step 5").* EF Core 10 on the API, `ExpensesDbContext` + `Household` entity, initial migration, `dotnet-ef` as a local tool (`.config/dotnet-tools.json`), LocalDB for dev, and Deploy now grants the API identity's DB user and applies migrations before switching the image.
- [~] **6. Household members, sign-in with Microsoft and Google, invitations, Parent/Child roles** — *Microsoft sign-in built and **deployed** (2026-09-30); Google deferred; real browser sign-in test still pending.* API: token validation, Data Protection cookie sessions, first-sign-in bootstrap, Parent/Child authorization, members + invitations endpoints. Web: MSAL sign-in, dashboard (members, invitations with shareable links). Infra: Key Vault data-protection key + Crypto User + dataprotection blob container. Prod `/health` green; unauthenticated auth endpoints return 401.
- [~] **7. Payment methods and categories** — *built + tested locally (2026-10-08); not deployed.* Entities, migration, Parent-only endpoints (`/payment-methods`, `/categories`), default category seed/backfill, web Settings tab.
- [~] **8. Manual expense entry with line items, "for" tagging, value tags, and notes** — *built + tested locally (2026-10-08); not deployed.* `Expense`/`LineItem`/`Merchant`/`ValueTag`, `POST/GET /expenses`, web entry form + list. Expense edit/delete not yet built.
- [x] **9. Reports (basic)** — *built + tested locally (2026-10-08); pushed.* `GET /reports/summary`, web Reports tab. Budget-vs-actual deferred to step 12 (budgets).
- [~] **10. Item database** — *lite version done as part of step 8:* `Item` + `ItemReceiptDescription` populated by manual entry, with the app figuring out the short form. Still to do: item merge, pictures, editing.
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
- **Deploy applies migrations** via `dotnet ef migrations script --idempotent` + `sqlcmd` (`--authentication-method ActiveDirectoryDefault`), after building the image and before switching it in. Each SQL script is retried (transient Azure SQL 40613 on first connection to an idle DB).
- **Confirmed on the first real run (2026-09-30):** OIDC/AAD auth works from the runner, and **GitHub-hosted runners reach the SQL server through the "Allow Azure services" (`0.0.0.0`) rule** — no firewall change needed. The first attempt failed only on transient 40613; switched from `azure/sql-action` (no retry) to the `sqlcmd` retry loop to handle it. Re-run pending.

### Notes for step 6 (sign-in)

**Scope decided 2026-09-30 (SPEC decisions #43–46):**
- **Parents only sign in.** Children are member records with no login (created by a parent, used for "for" tagging in step 8). No child auth, no ages needed.
- **Microsoft first, Google later.** Build and verify Microsoft (MSAL PKCE) end to end, then add Google Identity Services in a follow-up.
- **First sign-in bootstraps the household:** the first person to sign in becomes a Parent and their household is created; everyone else joins via invitation.
- **Invitations are shareable links/codes** the parent sends themselves. No email in step 6 (Communication Services deferred to step 12).

**Entra app registration: done (2026-09-30).** Created via az CLI (see FAQ → Sign-in and identity). Client ID `7c5331e4-7ad2-4880-bbec-597b6338036f`, SPA, personal + org accounts, no secret, redirect URIs for localhost and the prod SWA. This client ID is non-secret and goes in config (web build var + API token-audience check).

**Already-decided technical constraints:**
- **No client secrets** (SPEC #39). The browser gets an ID token from the provider via MSAL PKCE; the API validates it against the provider's public keys, applies the bootstrap/invitation rules, and starts its own session. Do **not** use `AddGoogle` / `AddMicrosoftAccount` (they need a secret).
- Sessions use ASP.NET Core Data Protection with keys in Blob Storage, encrypted with a Key Vault key through the managed identity. Infra changes needed then:
  - Add a Key Vault key, and give the API identity **Key Vault Crypto User** (replacing Key Vault Secrets User, which is no longer needed).
  - Add that role ID to `$assignableRoleIds` in `infra/bootstrap.ps1`, **re-run the bootstrap script**, then run `apply`. Verify role IDs with `az role definition list --name "<role>"` and never type them from memory (see FAQ).

**Built (code committed locally, pending deploy):**
1. ✅ Data model: `Member`, `Invitation`, migration.
2. ✅ Session infra: Data Protection cookie; in Azure keys go to the `dataprotection` blob container encrypted by the Key Vault `dataprotection` key (Crypto User), via managed identity. `infra/main.bicep` passes `DataProtection__KeyVaultKeyId`.
3. ✅ Microsoft sign-in: `IExternalIdentityValidator`/`MicrosoftIdentityValidator` (multi-tenant + MSA, no secret), `AuthService` provisioning (bootstrap/invite/deny), `/auth/session|me|logout`, `Parent` policy.
4. ✅ Invitations: `/invitations` create/list/revoke (Parent-only), accept-by-code at `/auth/session`.
5. ✅ Web: MSAL sign-in (honors `?invite=<code>`), dashboard with members + invitations.

**Go-live status (2026-09-30):**
1. ✅ Re-ran bootstrap (deploy identity can assign Key Vault Crypto User).
2. ✅ Infrastructure `what-if` then `apply` — Key Vault `dataprotection` key, `dataprotection` blob container, Crypto User assignment, and `DataProtection__KeyVaultKeyId` on the Container App all created/set.
3. ✅ Pushed; Deploy `36749392814` shipped the new API image (`423457a`) and the web app. `/health` green; unauthenticated `/auth/me`, `/members`, and a bad-token `/auth/session` all correctly return 401.
4. ✅ **Browser sign-in verified (2026-09-30).** Signing in with Microsoft works end to end (Parent + household created on first sign-in). Fix along the way: the web app now uses the MSAL **redirect** flow with `handleRedirectPromise` (the popup flow left the auth code stranded in the URL); an `?invite=` code is preserved across the redirect via `sessionStorage`.

**Still to do for step 6:** Google sign-in (Identity Services) in a follow-up; no automated web tests yet.

**Known consideration:** the session cookie is cross-site (web on `azurestaticapps.net`, API on `azurecontainerapps.io`), set `SameSite=None; Secure`. Works in current browsers with CORS credentials; if a browser blocks third-party cookies it would break sign-in — fallback would be a bearer token or a Static Web Apps linked backend (needs the Standard tier, i.e. cost).

---

## Open questions and pending decisions

**From SPEC.md "Open Questions"** (still unanswered):
1. ~~Children's ages / supervised accounts.~~ **Answered 2026-09-30:** children don't sign in in phase 1 (SPEC #43), so this is moot for now.
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
| Entra app registration (web sign-in) | `Home Expenses web`, **client ID `7c5331e4-7ad2-4880-bbec-597b6338036f`** (object ID `c1106fbb-0c56-4951-83cd-1f140fd64238`). SPA, personal + org accounts, no secret. Redirect URIs: `http://localhost:5173`, `https://ashy-desert-0e2b1851e.4.azurestaticapps.net`. Authority `common`. (Non-secret; safe to commit.) |
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
  - Documented it all in FAQ → Database. Committed and **pushed** (`f94f221`); CI/Deploy triggered. The SQL steps still need the Deploy run checked to be considered verified.
  - Decided step 6 scope with the user: parents-only sign-in, Microsoft before Google, first-sign-in bootstraps the household, invitations as shareable links/codes (SPEC #43–46).
  - Built the step 6 data model (`Member`, `Invitation` + migration), committed locally.
  - Created the Microsoft Entra SPA app registration via az CLI (client ID `7c5331e4-…`, no secret); documented in FAQ → Sign-in and identity.
  - Verified step 5 in production (Deploy `36746336438`); fixed the deploy DB steps (go-sqlcmd + retries) and confirmed GitHub runners pass the SQL firewall rule.
  - Built step 6 (Microsoft sign-in): API auth/sessions/endpoints (+7 tests), Key Vault/session infra, and the web MSAL UI.
  - Deployed step 6: re-ran bootstrap, ran Infrastructure apply (KV key, Crypto User, dataprotection container, env var), pushed, Deploy shipped API+web. Auth endpoints smoke-tested (401 as expected).
  - Fixed web sign-in (MSAL redirect flow + handleRedirectPromise) and **verified Microsoft sign-in end to end in the browser**. Step 6 Microsoft path complete; Google sign-in still to do.
- **2026-10-08**:
  - Built **step 7** (payment methods + categories): entities, `AddPaymentMethodsAndCategories` migration, Parent-only endpoints, default category seed on household bootstrap + idempotent backfill when a parent reads an empty list, web Settings tab.
  - Built **step 8** (manual expense entry): `Expense`/`LineItem`/`Merchant`/`ValueTag` entities, `AddExpenseEntry` migration, `POST/GET /expenses` (Parent-only writes; the total is summed from the lines), web Expenses tab with an entry form and recent-expenses list. Dashboard reorganized into Expenses / Settings / Household tabs.
  - Built a **lite item database** (step 10 start): `Item` + `ItemReceiptDescription` and a deterministic `ShortForm` generator, so typing a full item name makes the app "figure out the short form" and stores it per merchant for future receipt matching. No AI (SPEC #16).
  - Added tests: `ShortForm`, `ItemCatalog`, default-category seeding, and **end-to-end expense API tests** through `WebApplicationFactory` (new `TestApiFactory` with an in-memory DB + stub parent auth in a "Testing" environment). 23 tests pass; all migrations apply cleanly to LocalDB.
  - Recorded the design choices in SPEC Decisions #47–52 and added FAQ entries (Expense entry section).
  - Built **step 9** (basic reports): `GET /reports/summary` with period total and breakdowns by category / for / payment method / merchant / item / value tag (no schema change), web **Reports** tab with a date range. Added report API tests (25 tests total). SPEC #53; FAQ → Reports.
  - **Pushed** (user asked to "do the reports and push"): steps 7–9 go out together, CI → Deploy applies the two migrations and ships API + web. Browser verification against production still pending.
