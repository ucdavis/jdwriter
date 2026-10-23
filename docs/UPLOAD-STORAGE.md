# Shared upload storage

JDWriter uses one Standard_LRS StorageV2 account in `rg-jdwriter-prod` for temporary uploads.
The account name follows `main.bicep`, so a later full infrastructure deployment reuses it.
The upload application code is in [PR #70](https://github.com/ucdavis/jdwriter/pull/70).
Provisioning storage alone does not change the current upload flow.

| Setting | Value |
| --- | --- |
| Subscription | UC Davis CAES Production |
| Resource group | `rg-jdwriter-prod` |
| Region | `westus2` |
| Storage account | `stjdwriterprodwfwkuw` |
| Blob endpoint | `https://stjdwriterprodwfwkuw.blob.core.windows.net/` |
| Production container | `uploads` |
| Development container | `test` |

Both containers are private. Shared-key authentication is disabled. No account key, connection
string, client secret, or long-lived SAS needs to be handed to a developer.

## Access and configuration

The production App Service uses its system-assigned managed identity. It receives Storage Blob
Delegator on the account, which lets it obtain a user-delegation key, and Storage Blob Data
Contributor on `uploads`, which lets it read, write, and delete those blobs.

Each developer receives the same pair of roles, with data access scoped to `test` instead.
Storage Blob Delegator alone gives no access to blob contents. A SAS is limited by both its
permissions and the issuing principal's data permissions. See Microsoft's
[user-delegation SAS authorization documentation](https://learn.microsoft.com/rest/api/storageservices/create-user-delegation-sas).
Existing broader Azure role assignments can still confer additional access.

For local development, sign in with the granted UC Davis account using `az login`, then set these
values in the local development environment:

```dotenv
Uploads__BlobServiceUri=https://stjdwriterprodwfwkuw.blob.core.windows.net/
Uploads__Container=test
```

Leave `Uploads__ConnectionString` unset. The upload code gives a connection string precedence over
the endpoint; an Azurite connection string would send uploads to local storage instead.
`DefaultAzureCredential` uses the Azure CLI login locally and the managed identity on App Service.
Use synthetic or scrubbed documents in `test`.

Production settings use the same endpoint and `Uploads__Container=uploads`. Once the upload code
is deployed, these settings enable staging. Removing `Uploads__BlobServiceUri`, with no upload
connection string set, restores the direct-upload fallback implemented by PR #70.

## Deploy storage without redeploying the application

Run as an operator who can create storage resources and role assignments. The GitHub deployment
identity's Contributor role cannot create role assignments. The standalone template requires an
existing production App Service with a system-assigned identity and checks the subscription and
resource group before creating resources.

Resolve each developer's Entra **user object ID**, not an application client ID:

```bash
az ad user show --id <developer-email> --query id -o tsv
```

Preview and deploy the same parameters:

```bash
az deployment group what-if \
  --resource-group rg-jdwriter-prod \
  --template-file infrastructure/azure/upload-storage.bicep \
  --parameters expectedSubscriptionId=003283b1-cc5e-417a-b037-01ff3c05537b \
    'testUserPrincipalIds=["<developer-object-id>"]'

az deployment group create \
  --resource-group rg-jdwriter-prod \
  --name jdwriter-upload-storage \
  --template-file infrastructure/azure/upload-storage.bicep \
  --parameters expectedSubscriptionId=003283b1-cc5e-417a-b037-01ff3c05537b \
    'testUserPrincipalIds=["<developer-object-id>"]'
```

Use an empty array when provisioning without developer grants. Check the deployment output
`deploymentGuardPassed` is `true`. This is an incremental deployment; removing someone from the
parameter array does not revoke an existing role assignment. Revoke both of that person's scoped
roles explicitly when removing access.

`main.bicep` uses the same modules. Its optional `assignStorageRoles=true` grants the app the same
two roles when run by an authorized operator. It defaults to `false` for the GitHub identity.
Do not run the separate test infrastructure deployment merely to test uploads. Configure a local
app to use the shared account's `test` container.

## Browser access and cleanup

CORS permits `PUT` and `OPTIONS` from these exact origins:

- `https://people.caes.ucdavis.edu`
- `https://web-jdwriter-prod-wfwkuw.azurewebsites.net`
- `http://localhost:5173`
- `http://127.0.0.1:5173`

Allowed request headers are `content-type` and `x-ms-*`. CORS applies to the account's blob
service, so both containers share these origins. Authentication and container-scoped roles provide
the access boundary. Keep `uploadCorsOrigins` consistent in both entry points if adding an origin.

The browser receives a ten-minute HTTPS SAS for one randomly named blob, with create and write
permissions only. SAS links are bearer credentials and remain reusable until expiry. Do not call
them single-use unless the application also tracks and consumes upload sessions. Do not log SAS
URLs or original HRTMS filenames.

The application must read and delete staged bytes promptly. Both `uploads/` and `test/` have a
lifecycle rule that makes block blobs eligible for deletion after one day. Azure processes lifecycle
rules asynchronously, so this is a backstop and not a promise to delete within 24 hours. See
[lifecycle timing](https://learn.microsoft.com/azure/storage/blobs/lifecycle-management-performance-characteristics).
Blob and container soft delete, versioning, and change feed are explicitly disabled for this
disposable staging account so deleted contents are not retained in those features.

## Verify before calling the application migration complete

Verify a synthetic file can be uploaded with a write-only user-delegation SAS, that SAS reads and
anonymous reads fail, and the signed-in test principal can read and delete it. Check browser CORS
for both a permitted and an unpermitted origin. Confirm the test principal cannot write to
`uploads`, and remove smoke-test blobs afterwards.

Those checks establish storage readiness. After the application code deploys, verify the actual
browser flow on the production hostname: stage link, direct blob PUT, application processing, and
deletion. A successful resource deployment or managed-identity role assignment alone does not
prove that flow.
