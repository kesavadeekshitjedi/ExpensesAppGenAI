# Home Expenses App — Project Spec

> Drafted 2026-09-15 from a Q&A session. Items under **Decisions Log** were confirmed by the user.
> Items marked **(Proposed)** are suggestions that still need confirmation. Open items are listed under **Open Questions**.

---

## Overview

A family expense tracker, in the spirit of Quicken, that helps the household:

- **Track** spending through manual entry and receipt photos that are itemized, including who each item was for.
- **Predict** upcoming spending and bills.
- **Prevent wasteful spending** through budgets, alerts, price tracking, and the family's own value tags on each item.

Family members sign in with their personal **Outlook.com (Microsoft)** or **Gmail (Google)** accounts.

---

## Phases

| Phase | Scope |
|---|---|
| 1 | ASP.NET Core API + React web app, hosted on Azure and deployed by GitHub Actions. Manual entry, receipt capture, item database, tagging, budgets, recurring bills, alerts (in-app + email), reports, predictions. |
| 2 | React Native iOS app using the same API. Push notifications. |
| 3 | Automated tracking (e.g., bank/card sync, emailed receipts). Designed only after phases 1 and 2 are functional. |

In phase 1, receipts are photographed by opening the web app in a phone browser, which can use the camera directly.

---

## Tech Stack

| Layer | Choice |
|---|---|
| API | C# ASP.NET Core Web API (.NET 10) |
| Web | React, Vite + TypeScript |
| Mobile (Phase 2) | React Native |
| Database | Azure SQL Database with Entity Framework Core |
| Receipt and item images | **(Proposed)** Azure Blob Storage |
| Receipt reading (text extraction only) | **(Proposed)** Azure AI Document Intelligence, prebuilt receipt model |
| Email | **(Proposed)** Azure Communication Services Email or a similar provider |
| Hosting | Azure, keeping monthly cost low. Web: Azure Static Web Apps (free tier). API: Azure Container Apps (consumption, scales to zero) with images in Azure Container Registry (Basic). Region: West US 2 |
| Source control | Private GitHub repo |
| CI/CD | GitHub Actions |

There is **no AI interpretation of items** in phase 1. Receipt reading only extracts the printed text; the app identifies items through its own item database and asks the user when it can't (see Feature 3).

Current Azure pricing, free-tier limits, and GitHub Actions minute allowances for private repos must be checked at setup time; they change.

---

## Users, Login, and Permissions

### Sign-in
- Microsoft personal accounts (Outlook.com) and Google accounts (Gmail).
- **No client secrets, anywhere.** Sign-in must not require a client secret for Microsoft or Google. **(Proposed)** design:
  - The web app (and later the iOS app) gets a signed **ID token** directly from the provider using public-client flows that have no secret: Microsoft via MSAL with PKCE (app registered as a single-page app, personal Microsoft accounts allowed); Google via Google Identity Services "Sign in with Google" (returns an ID token to the browser).
  - The app sends that ID token to the API once. The API validates it against the provider's public signing keys (issuer, audience = our client ID, expiry), checks the email has an invitation, and starts its own session.
  - API sessions are protected with ASP.NET Core Data Protection. Its keys are stored in Blob Storage and encrypted with a Key Vault key, both reached through the API's managed identity, so no signing key or secret appears in configuration.
  - ASP.NET Core's server-side external login handlers (`AddGoogle`, `AddMicrosoftAccount`) are **not** used, because they require a client secret.
- **Invitation only.** A person can only sign in if a parent has invited that email address. A valid Google or Microsoft account alone is not enough.
- The auth design must work for the React web app now and the React Native app in phase 2 (token-based access for mobile).

### Roles
| Role | Can see | Can do |
|---|---|---|
| Parent | Everything in the household | Manage members, invitations, payment methods, categories, items, budgets, recurring bills; view and edit all expenses |
| Child | Everything in the household (for now) | **View only.** Cannot enter, edit, or tag expenses, add items, or change any settings |

Children seeing everything is the current rule. Visibility could be narrowed later (e.g., to line items tagged for them); the per-line "for" tag already stores what that would need, so no data changes would be required.

### Kids' accounts
- **(Proposed)** A household member does not need a login. Every child and parent can exist as a member (so line items can be tagged for them), and only members who are invited can sign in.
- Google and Microsoft apply supervised-account restrictions to children under 13, which may block sign-in to third-party apps. Verify this for the family's actual accounts before building login.

---

## Features

### 1. Payment Methods
- Parents define payment methods as **labels only**, e.g., "Discover card", "Citi bank account", "Cash".
- Each has a label and a type (credit card, bank account, cash, other).
- **No card numbers, account numbers, or other financial credentials are ever stored.** The app must not have fields for them.
- No balances are tracked.

### 2. Expenses (Manual Entry)
Each expense records:
- Date
- Merchant (e.g., Costco)
- Entered by (a parent)
- **(Proposed)** Default "for" value, applied to every line item and changeable per line
- Payment method
- Total, plus tax if known
- Notes
- Source: `manual` or `receipt`
- One or more **line items**

Each line item records:
- Description as entered or as printed on the receipt
- Linked **item** from the item database (see Feature 3), when known
- **For**: who the item was for — a specific household member (e.g., Kid 1, Kid 2, Parent 1, Parent 2) or **Family**
- Category
- Quantity, unit price, amount
- **Value tag** (see Feature 4)
- **Notes**: free text explaining the purchase (e.g., "needed for school project", "splurge, didn't need it", "replaces broken one"). Stored with the line item and kept permanently.

A simple expense (e.g., a single restaurant bill) is one line item.

Only parents can enter or edit expenses. Because "for" is set per line item, one receipt can cover several people (e.g., a Costco trip with groceries for the family and shoes for Kid 1).

### 3. Receipt Capture and the Item Database

The app does not try to understand what an item is. It keeps a household **item database** built from the user's own answers.

**Item database**
- An **item** has: full name (e.g., "Kirkland Signature Organic Eggs, 24 ct"), default category, and an optional **picture of the actual item**.
- Each item has one or more **receipt descriptions**, each tied to a merchant (e.g., Costco: "KS ORG EGGS").
- Parents can view, edit, merge, and add pictures to items at any time.

**Receipt flow**
1. User photographs or uploads a receipt.
2. The receipt image is stored; Document Intelligence extracts merchant, date, total, tax, and the printed line items.
3. For each line, the app looks up the merchant + printed description in the item database.
   - **Match found:** the line is filled in with the item's full name and category.
   - **No match:** the app **asks the user what the item is**. The user can:
     - pick an existing item (e.g., the same eggs printed differently), or
     - enter a new full name and category, and optionally attach a picture of the item.
   - Either answer is saved to the item database, so that description is recognized automatically next time.
4. User reviews the complete **draft expense**, sets who each line was for, corrects anything, and saves.

**Rules**
- **Nothing is saved from a receipt without the user's review.**
- Unmatched lines are clearly highlighted; the user can also skip naming an item and save the line with just its printed description, to be identified later.
- If extracted line items don't add up to the total, the draft shows the difference clearly.
- Item pictures are resized before storage **(Proposed)** to keep storage cost and page load low.

### 4. Value Tags
- Each **line item** can have a value tag describing how the family judges that purchase.
- **No predefined tags.** The app starts with an empty tag list.
- When tagging a line item, a parent either picks an existing tag or types a new one. A new tag is **saved to the tag table immediately** and offered for reuse on every later line item.
- While typing, existing tags are suggested, so the same tag is reused instead of re-created.
- **(Proposed)** Tag names are unique per household, ignoring upper/lower case and extra spaces, so "Splurge" and "splurge " are the same tag.
- **(Proposed)** Parents can rename or merge tags later; renaming updates every line item using that tag.
- Only parents apply or create tags.
- **(Proposed)** An item can have a default tag, applied automatically when it's recognized on a receipt and changeable per line.
- Reports show spending by tag (e.g., total for each tag this month).

### 5. Categories
- A default category list (e.g., groceries, dining out, utilities, household, transportation, entertainment, health, kids, subscriptions).
- Parents can add, rename, and archive categories.

### 6. Budgets and Alerts
- Monthly budget per category.
- Alert when spending reaches a threshold (**Proposed:** 80% and 100%).
- Alerts are delivered **in-app and by email** in phase 1, and by **push notification** in phase 2.
- **(Proposed)** Each user can choose which alerts they receive by email.

### 7. Spending Flags

The app raises **flags** for spending that deserves a second look. A flag is a signal to review, not a block. Every flag type is **color coded** and appears the same way everywhere: in expense lists, on line items, in reports, and in alerts.

**Flag types**

| Flag | Rule | Why it matters |
|---|---|---|
| Person over monthly threshold | Line items tagged **for** one person (a parent or child) total more than **$500 in a calendar month** | Possibly unintended spending, especially for children |
| Large Family item | A single line item tagged **Family** with an amount over **$500** | Family spending is usually intended, but one large purchase should still be reviewed |
| Out of pattern | **(Proposed)** An expense far above the typical amount for its category, or a large first-time purchase at a new merchant | Catches unusual spending not covered by the rules above |

**Family spending**
- Family items are fully tracked and reported. A Family **monthly** total over $500 is expected and is **not** flagged.
- A receipt whose Family items total more than $500 is also **not** flagged (e.g., a large Costco grocery run).
- Only a **single Family line item** over $500 is flagged (e.g., a $700 TV).
- Family items do not count toward any individual person's monthly total.

**Per-person flag details**
- Applies to every household member, and is especially important for children.
- Raised the moment an expense pushes a person over the threshold. Shows that person's month broken down by category and item.
- **(Proposed)** Raised once per person per month, not again on every later purchase that month.

**Color coding (Proposed)**
- Each flag type has its own color, used consistently across the web app (and later the iOS app), e.g., red for a person over the monthly threshold, amber for a large Family item, blue for out of pattern.
- Color is always paired with an icon and a text label, so flags are readable without relying on color alone.
- A flag stays visible until a parent marks it **reviewed**, optionally with a note; reviewed flags keep their color in a muted form.

**Delivery**
- Flags generate alerts in-app and by email to parents.
- **(Proposed)** Both $500 thresholds are household settings parents can change, with an option to set a different monthly amount per person.

### 8. Recurring Subscriptions and Bills
- Track recurring items: name, amount, frequency, next due date, category, payment method.
- Show upcoming bills.
- **(Proposed)** Suggest possible recurring items when the same merchant and similar amount repeat on a regular schedule; user confirms.

### 9. Item Price Comparison
- Track the price of the same **item** across receipts over time, using the item database (e.g., price of eggs at Costco by month).
- Show price history and highlight increases.
- Works across different printed descriptions, because they all link to the same item.

### 10. Predictions
- Next month's spending by category.
- Upcoming recurring bills.
- Year-end spending projection.
- Forecasts require a minimum amount of history. **(Proposed:** 3 months.) Until then the app states that there isn't enough data instead of showing numbers.
- **(Proposed)** Simple statistical methods (averages and trends from past months plus known recurring bills).
- **Line item notes** are an input for predictions and waste insights. For example, repeated notes like "splurge" or "didn't need it" on the same item or category indicate spending likely to recur and worth reducing. The method for using notes (e.g., keyword rules vs. AI summarization) is decided when this feature is built — see Open Questions.

### 11. Reports
- Spending by category, **who it was for** (including Family), payment method, merchant, item, and value tag, over a chosen period.
- Flagged spending shown with its flag color; filter by flag type and reviewed/unreviewed.
- Search and view line item notes.
- Budget vs. actual.
- Parents and children see the same household-wide reports (children view only).

---

## Data Model (Proposed)

| Entity | Key fields |
|---|---|
| Household | name |
| Member | household, display name, role (Parent/Child), email and login provider (optional; only for members who can sign in) |
| Invitation | household, email, role, status, expires |
| PaymentMethod | household, label, type, archived |
| Category | household, name, archived |
| ValueTag | household, name (unique per household, case-insensitive), created by, created at |
| Merchant | household, name |
| Item | household, full name, default category, default value tag, picture location |
| ItemReceiptDescription | item, merchant, printed description |
| Expense | household, entered by (member), merchant, payment method, date, total, tax, notes, source |
| LineItem | expense, printed description, item (nullable), category, for (a member, or Family), quantity, unit price, amount, value tag, notes |
| Flag | household, type, member (for per-person flags), line item (for large Family item flags), expense (for out-of-pattern flags), month, amount, created, reviewed by, reviewed at, review note |
| Receipt | expense, image location, extraction status, extracted data |
| Budget | household, category, month, amount, alert thresholds |
| RecurringBill | household, name, amount, frequency, next due, category, payment method |
| Alert | member, type, message, created, read, emailed |
| HouseholdSettings | household, per-person monthly spending threshold (default $500), single Family line item threshold (default $500) |

- Every query must be scoped to the signed-in user's household. Children can read all household data; all write operations from a child are rejected by the API.
- `ItemReceiptDescription` is unique per merchant + printed description.

---

## Deployment Process

### Environments
- A single **production** environment on Azure. **No staging environment.**
- Local development runs the API and web app on the developer's machine against a local database.
- Because there is no staging, CI tests and the post-deploy smoke test are the safety net; Static Web Apps pull-request preview environments are turned off so nothing points test builds at production data.

### Azure resources
| Resource | Purpose |
|---|---|
| Resource group `rg-expenses-prod` | Holds everything for the app |
| Resource group `rg-expenses-bootstrap` | Holds only the GitHub deploy identity; never deleted, so the app resource group can be rebuilt |
| Azure Static Web Apps | React web app |
| Azure Container Apps (consumption) + Container Registry (Basic) | ASP.NET Core API |
| Azure SQL Database (Basic, 5 DTU; Entra-only sign-in) | App data |
| Storage account (Blob) | Receipt images and item pictures |
| Azure AI Document Intelligence | Receipt text extraction |
| Email service | Alert emails |
| Key Vault | The key that encrypts the API's session-protection keys. No secrets: services are reached through managed identity, and sign-in uses no client secrets |
| Application Insights | Logs and errors |

All resources are defined in **Bicep** files in the repo (`infra/`) and created or updated by a manually triggered workflow, so the environment can be rebuilt from scratch. Resources are deployed as an Azure **deployment stack**, so anything removed from the templates is deleted on the next apply. A one-time script (`infra/bootstrap.ps1`) creates the deploy identity, its permissions, and the SQL admin group before the workflow can run.

### Azure authentication from GitHub
- GitHub Actions signs in to Azure with **OpenID Connect (federated credentials)** on a Microsoft Entra app registration or managed identity.
- **No Azure passwords or publish profiles are stored in GitHub.** GitHub only holds non-secret identifiers (tenant ID, subscription ID, client ID).
- The identity is given the minimum role needed, scoped to the app's resource group: Contributor, plus the right to assign only the specific roles the templates use. At subscription level it has one narrow custom role that only allows purging soft-deleted Key Vaults and Document Intelligence resources (needed by `recreate`).
- The API reaches every Azure service through its managed identity, so there are no app secrets to store.

### Workflows

**1. CI — `ci.yml`**
- Trigger: every pull request and push to `main`.
- API: restore, build, run tests.
- Web: install dependencies, lint, run tests, build.
- A pull request cannot be merged unless CI passes **(Proposed:** branch protection on `main`).

**2. CD — `deploy.yml`**
- Trigger: push to `main` (i.e., after a PR is merged), plus manual run.
- Only runs if CI passes.
- Steps, in order:
  1. Sign in to Azure via OIDC.
  2. Build and publish the API.
  3. **Apply database migrations** using an EF Core migration bundle. If migrations fail, the deploy stops and the API is not updated.
  4. Deploy the API.
  5. Build the web app with the production API address and deploy it to Static Web Apps.
  6. Run a **smoke test**: call the API health endpoint and load the web app. The workflow fails visibly if either is down.
- **(Proposed)** Only deploy the parts that changed (API changes don't redeploy web, and vice versa).

**3. Infrastructure — `infra.yml`**
- Trigger: manual only.
- Three modes:
  - `what-if`: preview of creates and changes only.
  - `apply`: creates or updates resources through the deployment stack; resources removed from the templates are deleted.
  - `recreate`: deletes every app resource, purges soft-deleted Key Vault and Document Intelligence resources, then rebuilds. Requires typing `DELETE rg-expenses-prod`. **All data is lost**; a database and receipt-image export/import step must be added before this is used with real data.

**4. Mobile (Phase 2)**
- iOS builds and App Store/TestFlight distribution will be added in phase 2 (e.g., via Expo EAS or Xcode Cloud). Not designed yet.

### Database migration rules
- Migrations are created locally and committed with the code change that needs them.
- Migrations must be backward-compatible with the currently running API version, because migrations run just before the new API is deployed (e.g., add a column first; remove the old one in a later release).
- **(Proposed)** Azure SQL automated backups are the rollback path for data; confirm the retention period at setup.

### Rollback
- Code rollback: revert the commit on `main`, which triggers a normal deploy of the previous version.
- Container Apps can also switch traffic back to a previous revision if a quick rollback is needed.

### Secrets and configuration
- **Goal: no secrets exist at all**, rather than secrets that are stored carefully.
  - GitHub → Azure: OpenID Connect (federated credential); no passwords or publish profiles.
  - API → SQL, Blob Storage, Key Vault, Document Intelligence, Email: the API's managed identity; no connection-string passwords or API keys (local key auth is disabled where the service allows it).
  - Family sign-in: public-client flows with no client secret (see Users, Login, and Permissions).
- Configuration values (endpoints, client IDs, origins) are not secret and are set as Container App environment variables by Bicep.
- Local development: the API signs in to any Azure services as the developer through `az login` (DefaultAzureCredential); the web app's `.env.development` holds only non-secret URLs.
- GitHub: only the OIDC identifiers, stored as environment variables.
- **Nothing secret is ever committed**, and any new feature that would need a secret must first look for a managed-identity or public-client alternative.

---

## Repository Structure (Proposed)

```
ExpensesAppGenAI/
  SPEC.md
  README.md
  .gitignore
  .github/
    workflows/
      ci.yml
      deploy.yml
      infra.yml
  infra/          ← Bicep files
  src/
    api/          ← ASP.NET Core Web API
    web/          ← React web app
    mobile/       ← React Native app (Phase 2)
  tests/
    api.tests/
```

- The GitHub repo must be **private** before any code is pushed.
- `.gitignore` must exclude build output, `node_modules/`, `.env` files, user secrets, and any local receipt or item images.

---

## Build Order for Phase 1 (Proposed)

Deployment is set up early, so every later step ships to Azure through the pipeline.

1. Repo setup, solution structure, empty API (with health endpoint) and React app running locally
2. CI workflow
3. Azure resources and OIDC sign-in from GitHub
4. CD workflow deploying the empty app to Azure
5. Database, core entities, and migrations in the pipeline
6. Household members, sign-in with Microsoft and Google, invitations, Parent/Child (view-only) roles
7. Payment methods and categories
8. Manual expense entry with line items, "for" tagging, value tags, and notes
9. Reports (basic)
10. Item database
11. Receipt capture, item matching, and "what is this item?" prompts
12. Budgets and alerts (in-app, then email)
13. Recurring bills
14. Item price comparison
15. Spending flags (per-person monthly, large Family item, out of pattern) with color coding
16. Predictions

---

## Teaching Approach

- Built **step by step**; the assistant explains concepts and guides each step.
- **The user writes the code**, including workflow and infrastructure files. The assistant does not write code unless the user explicitly asks.

---

## Open Questions

1. Ages of children, and whether any use supervised Google/Microsoft accounts (if children sign in at all).
2. Besides household members and Family, are other "for" values needed (e.g., grandparents, pets, gifts)?
3. Do children receive any alerts or emails, or only see alerts when viewing the app?
4. How are returns and refunds recorded? (Affects whether a refund lowers a person's monthly total.)
5. Currency: single currency only?
6. How long are receipt images kept?
7. Should expenses support attachments other than receipts (e.g., warranty PDFs)?
8. How should line item notes be used for predictions: simple keyword rules, or AI summarization? (Decision #16 rules out AI for *identifying items*; this is a separate question.)
9. Is a Windows client wanted in addition to iOS? If so, what tech (React Native Windows, MAUI/WinUI, or just relying on the responsive web app / installed PWA)? Not currently in any phase — the web app hosting choice does not block this either way, since native/desktop clients call the API directly rather than going through Static Web Apps.

---

## Decisions Log

| # | Decision |
|---|---|
| 1 | Family expense tracking, prediction, and waste prevention; Quicken as the reference |
| 2 | Sign-in with Outlook.com (Microsoft) and Gmail (Google) accounts |
| 3 | Phase 1: web app. Phase 2: iOS app |
| 4 | Data entry starts with manual entries and receipt photos that are itemized |
| 5 | Automated tracking is designed after the web and iOS apps are functional |
| 6 | Parents see all household data. (Originally children saw only their own; superseded by #22) |
| 7 | Stack: C# API backend, React web, React Native iOS |
| 8 | Taught step by step; the user writes the code |
| 9 | Waste prevention: budgets with alerts, unusual-spending flags, recurring bill tracking, item price comparison, and user value tags |
| 10 | Predictions: next month by category, upcoming recurring bills, year-end projection |
| 11 | Hosted on Azure, keeping monthly cost low |
| 12 | Payment methods are labels only (e.g., "Discover card"); no card or account details stored |
| 13 | Value tags apply per line item |
| 14 | Alerts: in-app and email in phase 1; push notifications with the iOS app |
| 15 | Code lives in a private GitHub repo with GitHub Actions for CD |
| 16 | No AI interpretation of items. The app matches receipt descriptions against its item database and asks the user when an item is unknown |
| 17 | The user supplies an unknown item's full name, which is saved to the item database with an optional picture of the actual item |
| 18 | Single production environment; no staging environment |
| 19 | Azure infrastructure is defined in Bicep files |
| 20 | Children are view-only: they cannot enter, edit, or tag expenses |
| 21 | Each line item is tagged with who it was for: a specific household member (e.g., Kid 1, Parent 2) or Family |
| 22 | For now, children can view everything in the household (still view-only) |
| 23 | No per-person budgets |
| 24 | Flag any person whose spending exceeds $500 in a month, especially children |
| 25 | Family items are tracked and flagged too; Family spending is treated as likely intended |
| 26 | Family totals over $500 (monthly or on one receipt) are OK; only a single Family line item over $500 is flagged |
| 27 | All flag types are color coded and shown consistently |
| 28 | Each line item can have notes, stored permanently, used to understand purchases and infer future expenses |
| 29 | No predefined value tags; any tag a parent creates is saved to the tag table and reused |
| 30 | Web build tooling: Vite + TypeScript |
| 31 | Database: Azure SQL Database with Entity Framework Core |
| 32 | Web hosting: Azure Static Web Apps (free tier). This does not affect whether a Windows or iOS client can be added later — native/desktop clients call the API directly, not through Static Web Apps (see Open Question #9) |
| 33 | Target monthly Azure budget: $50–100 |
| 34 | GitHub repo: https://github.com/kesavadeekshitjedi/ExpensesAppGenAI (private) |
| 35 | API hosting: Azure Container Apps (consumption, scale to zero; cold starts accepted) |
| 36 | Azure SQL tier: Basic (5 DTU) |
| 37 | Azure region: West US 2 |
| 38 | GitHub deploy identity lives in its own resource group (`rg-expenses-bootstrap`); app infra is deployed as a deployment stack; `infra.yml` has a `recreate` mode (delete and rebuild, with typed confirmation) |
| 39 | Never use client secrets. Azure access uses OIDC and managed identities; family sign-in uses public-client flows (MSAL with PKCE, Google Identity Services ID tokens) validated by the API |
