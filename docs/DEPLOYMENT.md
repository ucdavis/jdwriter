# Deploying JDWriter

The template's Azure guide ([README.customization.md](../README.customization.md#5-azure-deployment-setup)
and [infrastructure/azure/README.md](../infrastructure/azure/README.md)) covers the general path:
Entra app registration, the one-time OIDC bootstrap, GitHub Environments, Configure Azure, then
CI/CD. This page lists only what JDWriter adds on top, in the order you will need it.

## 1. GitHub Environment settings (`test` and `prod`)

Besides the template's `AZURE_*` variables and `SQL_ADMIN_PASSWORD`:

| Name | Kind | Value |
|---|---|---|
| `ADMIN_BOOTSTRAP_LOGIN_IDS` | variable | Comma-separated UC Davis login IDs that are always admins, e.g. `rsmith`. **Required** — Configure Azure refuses to run without it, because an app with no admin cannot be administered. These admins cannot be removed from the app; further admins are added on `/backend/settings`. |
| `LLM_PROVIDER` | variable | `anthropic` (default), `azure-openai`, or `openai-compatible`. Which provider receives JD text is decided here, per environment — never in the app. |
| `LLM_MODEL` | variable | Azure OpenAI **deployment name**, or model id. Optional for Anthropic. |
| `LLM_ENDPOINT` | variable | `https://NAME.openai.azure.com` for Azure OpenAI; a base URL ending in `/v1` for an OpenAI-compatible server. |
| `LLM_SEND_REASONING_EFFORT` | variable | `true` only for reasoning deployments (o-series, GPT-5); other models reject the parameter. |
| `LLM_ALLOW_KEY_ENTRY_IN_APP` | variable | `false` to keep keys in Key Vault only (Settings then shows the source but accepts nothing). Default `true`. |
| `ANTHROPIC_API_KEY` / `AZURE_OPENAI_API_KEY` / `OPENAI_API_KEY` | **secret** | The active provider's key — preferably a Key Vault reference (below), not the key itself. Where in-app entry is allowed, an admin can also enter or rotate one on `/backend/settings`; it is encrypted by the app, audited, and takes precedence while set. |

All are declared in `infrastructure/azure/deployment-settings.json` and applied to the App Service by
the **Configure Azure** workflow. Run it after setting or changing them.

### Keys in Key Vault

Set the key secret's value to a Key Vault reference instead of the key:

```
@Microsoft.KeyVault(SecretUri=https://VAULT.vault.azure.net/secrets/azure-openai-key/)
```

Configure Azure writes that string to the App Service setting, and App Service resolves it at
runtime; the key itself never passes through GitHub. Leave the version off the URI so a rotation in
the vault is picked up (within 24 hours, or on restart). One-time, per environment: grant the web
app's **system-assigned identity** the *Key Vault Secrets User* role on the vault:

```bash
az role assignment create --role "Key Vault Secrets User" \
  --assignee "$(az webapp identity show -g RG -n APP --query principalId -o tsv)" \
  --scope "$(az keyvault show -n VAULT --query id -o tsv)"
```

The App Service's *Environment variables* blade shows a green check beside a resolved reference.
If it is red, the app sees the literal string, the provider rejects it, and Settings shows the
configured key as the reference's last four characters — the clue to look at the role assignment.

For a fully vault-managed environment, set `LLM_ALLOW_KEY_ENTRY_IN_APP=false` too: no key can then
be entered in the app, and one stored earlier is ignored.

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
  - **AI provider** should name the intended provider and model, and its key should show the
    configured key's last four characters.
  - **Key changes** lists every in-app change to the key — who and when, never the value.
- Add the other admins by login ID.

## Where sensitive data lives

| Data | Where | Protection |
|---|---|---|
| JD corpus, uploaded HRTMS exports (position numbers, reporting lines) | Azure SQL only — never the App Service disk | TDE (encryption at rest); admin-only endpoints |
| Saved JDs, envelopes | Azure SQL | TDE |
| Provider key entered in the app | Azure SQL | Encrypted by ASP.NET Core Data Protection before storage, plus TDE; write-only; one per provider; every change audited (who, when, last four) |
| Provider key from configuration | Key Vault, via an App Service Key Vault reference (recommended), or an App Service setting | Vault access by the app's managed identity only; never returned or logged |
| JD text sent for assembly | The configured provider | Anthropic or Azure OpenAI under campus agreements, or a local model that keeps it on college infrastructure — chosen per environment |

The app's Data Protection keys live on the App Service file system (the template's default), which
persists across restarts and deployments. If the App Service is ever recreated those keys are lost:
the app keeps working on the configured key, and Settings asks for the in-app key to be entered again.
Move the key ring to shared storage before scaling out to more than one instance.
