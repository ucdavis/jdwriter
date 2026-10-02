# Deploying JDWriter

The template's Azure guide ([README.customization.md](../README.customization.md#5-azure-deployment-setup)
and [infrastructure/azure/README.md](../infrastructure/azure/README.md)) covers the general path:
Entra app registration, the one-time OIDC bootstrap, GitHub Environments, Configure Azure, then
CI/CD. This page lists only what JDWriter adds on top, in the order you will need it.

## 1. GitHub Environment settings (`test` and `prod`)

Besides the template's `AZURE_*` variables and `SQL_ADMIN_PASSWORD`:

| Name | Kind | Value |
|---|---|---|
| `ADMIN_BOOTSTRAP_LOGIN_IDS` | variable | Comma-separated UC Davis login IDs that are always admins, e.g. `ndlewis`. **Required** — Configure Azure refuses to run without it, because an app with no admin cannot be administered. These admins cannot be removed from the app; further admins are added on `/backend/settings`. |
| `ANTHROPIC_API_KEY` | **secret** | The Anthropic key. An admin can also enter or rotate a key on `/backend/settings`; that one is encrypted by the app and takes precedence while it is set. |

Both are declared in `infrastructure/azure/deployment-settings.json` and applied to the App Service by
the **Configure Azure** workflow. Run it after setting or changing them.

## 2. Turn on deployment

Until Azure is configured, pushes to `main` run tests (the Validate job) and skip deploying. Once the
`test` environment's variables exist, set the **repository** variable:

```bash
gh variable set DEPLOY_ENABLED --body true
```

From then on a push to `main` deploys to `test`; `prod` is a manual CI/CD run.

## 3. Load data

The corpus is loaded once per environment with the CLI, from a machine that has the POC checkout
(the HRTMS exports are not in this repository). Allow that machine's IP in the Azure SQL firewall
first.

```bash
# test — scrubbed: position numbers, reports-to, JD numbers, departments and export file
# names replaced, in fields AND in free text. Verified against all 1,367 exports.
dotnet run --project tools/jdw-cli -- migrate-poc --scrub --connection "<test connection string>"          # dry run
dotnet run --project tools/jdw-cli -- migrate-poc --scrub --write --connection "<test connection string>"

# prod — the real corpus. Prod only.
dotnet run --project tools/jdw-cli -- migrate-poc --write --connection "<prod connection string>"
```

`migrate-poc --write` replaces the corpus and the class profiles. It refuses to delete a class that
has saved JDs, by design — after go-live, add JDs with **Upload JDs** on `/backend` instead.

## 4. After the first deploy

- `/health` returns 200 and sign-in works (the template's checks).
- Sign in as a bootstrap admin and open **`/backend/settings`**:
  - **Data protection** should read **encrypted** — SQL Server reporting Transparent Data
    Encryption on. Azure SQL enables it by default and nothing here disables it. If it reads *not
    encrypted*, stop and enable TDE before loading real data.
  - **Anthropic API key** should show the configured key's last four characters.
- Add the other admins by login ID.

## Where sensitive data lives

| Data | Where | Protection |
|---|---|---|
| JD corpus, uploaded HRTMS exports (position numbers, reporting lines) | Azure SQL only — never the App Service disk | TDE (encryption at rest); admin-only endpoints |
| Saved JDs, envelopes | Azure SQL | TDE |
| Anthropic key entered in the app | Azure SQL | Encrypted by ASP.NET Core Data Protection before storage, plus TDE; write-only |
| Anthropic key from configuration | App Service setting | Azure platform encryption; applied from a GitHub Environment secret |

The app's Data Protection keys live on the App Service file system (the template's default), which
persists across restarts and deployments. If the App Service is ever recreated those keys are lost:
the app keeps working on the configured key, and Settings asks for the in-app key to be entered again.
Move the key ring to shared storage before scaling out to more than one instance.
