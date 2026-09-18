# Home Expenses — notes for Claude

Family expense tracker: ASP.NET Core API (.NET 10) + React (Vite + TypeScript), hosted on Azure, deployed by GitHub Actions.

## Start of every session

1. Read `docs/PROGRESS.md`. It has the current status, the next action, checklists, open questions, and key IDs.
2. `SPEC.md` is the source of truth for features and decisions (see its Decisions Log and Open Questions).
3. Before researching a how-to, check `docs/FAQ.md`. The answer may already be there.

## Working agreements with the user

- **Claude writes the code; the user reviews.** The spec's "Teaching Approach" (user writes code) is paused until the user says otherwise. The user is new to Vite and TypeScript, so briefly explain frontend choices.
- **Commit automatically** after each meaningful set of changes. **Push only when the user says so.**
- **Record every how-to / why question the user asks**, with a self-contained answer using this project's real names and IDs, in `docs/FAQ.md`.
- **Keep `docs/PROGRESS.md` current**: update the checklist, next actions, and session log whenever a step finishes or a decision is made.
- **No client secrets, keys, or passwords.** Use OIDC (GitHub → Azure), managed identity (API → Azure services), and public-client sign-in flows (MSAL PKCE, Google Identity Services ID tokens). If something seems to need a secret, find an alternative or flag it to the user first.
- **Ask the user** (don't assume) for decisions about cost, service tiers, or anything in SPEC's Open Questions.
- Verify Azure built-in role IDs with `az role definition list --name "<role>"`. Never write them from memory.

## Commands

```powershell
dotnet test ExpensesApp.slnx                      # API build + tests
dotnet run --project src/api                      # API on http://localhost:5080
cd src/web; npm install; npm run dev              # web on http://localhost:5173
cd src/web; npm run lint; npm run build
az bicep build --file infra/main.bicep            # compile infrastructure
```

Infrastructure changes go through GitHub **Actions > Infrastructure** (`what-if`, then `apply`). App deploys happen automatically after CI passes on `main`. Local equivalents are in `docs/FAQ.md`.

## Layout

- `src/api`: API. `src/web`: web app. `tests/api.tests`: xUnit tests.
- `infra/main.bicep` + `infra/modules/*`: Azure resources. `infra/bootstrap.ps1`: one-time identity/permissions setup (already run; re-run after changing `$assignableRoleIds`).
- `.github/workflows`: `ci.yml`, `deploy.yml`, `infra.yml`.
