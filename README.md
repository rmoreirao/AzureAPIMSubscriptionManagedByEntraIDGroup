# AzureAPIMSubscriptionManagedByEntraIDGroup

Self-service **team subscriptions** for Azure API Management, where access to a subscription's
keys is governed by membership of an **Entra ID group** rather than by a single owner.

## Architecture

The diagram below is exported automatically from [docs/diagram.drawio](docs/diagram.drawio) by a GitHub Actions workflow whenever the source file changes.

![Architecture diagram](docs/export/diagram.png)

### Azure resources

| Resource | SKU / Tier | Role |
|----------|-----------|------|
| API Management | **Developer** (Dev Portal enabled) | Developer portal + Custom API facade |
| Function App | Linux, **Consumption (Y1)**, .NET 8 isolated | Backend "Custom APIM API" |
| Cosmos DB | NoSQL, **Serverless**, `disableLocalAuth` | Team subscription store |
| Storage Account | Standard LRS | Functions runtime store |
| Application Insights + Log Analytics | Pay-as-you-go | Telemetry |

All service-to-service auth is **keyless** via **managed identity** + RBAC:

- Function MI → Cosmos DB (Built-in Data Contributor, data plane)
- Function MI → APIM (API Management Service Contributor, control plane)
- Function MI → Microsoft Graph (`GroupMember.Read.All`, `User.Read.All`)
- APIM MI → Function App (`authentication-managed-identity` policy)

## Repository layout

```
infra/        # Bicep IaC (main.bicep + modules)
src/          # .NET 8 isolated Azure Functions
.github/workflows/
  deploy-infra.yml   # Bicep deploy (OIDC)
  deploy-app.yml     # Function zip deploy (OIDC)
```

## Function endpoints

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/api/users/{userId}/groups` | List the user's Entra ID groups (Graph) |
| POST | `/api/team-subscriptions` | Create standalone APIM subscription + persist record |
| GET | `/api/team-subscriptions?groups=g1,g2` | List subscriptions for the caller's groups |
| POST | `/api/team-subscriptions/{entraIdGroup}/{subscriptionId}/{regenerate\|cancel}` | Regenerate keys or cancel |

## Prerequisites

- Azure subscription + an Entra ID tenant
- Azure CLI with the Bicep extension
- .NET 8 SDK
- A resource group (the infra workflow creates `rg-apimteam-dev` if missing)

### 1. Configure GitHub OIDC (no secrets)

Create an Entra app registration with a **federated credential** bound to this repo, then grant it
`Contributor` + `User Access Administrator` on the target resource group.

Add these **repository variables** (Settings → Secrets and variables → Actions → Variables):

| Variable | Description |
|----------|-------------|
| `AZURE_CLIENT_ID` | App registration (client) ID |
| `AZURE_TENANT_ID` | Entra tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Target subscription ID |
| `APIM_PUBLISHER_EMAIL` | Publisher email for APIM |
| `AZURE_FUNCTIONAPP_NAME` | Deployed Function App name (from infra outputs) |

### 2. Grant Microsoft Graph permissions to the Function identity

After the first infra deploy, grant the Function App's **system-assigned managed identity** the Graph
**application** permissions `GroupMember.Read.All` and `User.Read.All` (required by the `GetUserGroups`
endpoint), then **admin-consent** them. This requires a directory admin and cannot be done in Bicep.

Example (run as a tenant admin; `MI_OBJECT_ID` is the Function App identity's principal/object id):

```bash
MI_OBJECT_ID=$(az functionapp identity show -g <rg> -n <function-app> --query principalId -o tsv)
GRAPH_SP_ID=$(az ad sp show --id 00000003-0000-0000-c000-000000000000 --query id -o tsv)

# App role ids: GroupMember.Read.All = 98830695-27a2-44f7-8c18-0c3ebc9698f6
#               User.Read.All        = df021288-bdef-4463-88db-98f22de89214
for ROLE in 98830695-27a2-44f7-8c18-0c3ebc9698f6 df021288-bdef-4463-88db-98f22de89214; do
  az rest --method POST \
    --url "https://graph.microsoft.com/v1.0/servicePrincipals/$MI_OBJECT_ID/appRoleAssignments" \
    --body "{\"principalId\":\"$MI_OBJECT_ID\",\"resourceId\":\"$GRAPH_SP_ID\",\"appRoleId\":\"$ROLE\"}"
done
```

After granting, restart the Function App so its managed-identity token picks up the new roles.

## Deploy

Deployment is split into two workflows, both triggered on push to `main` (or via
`workflow_dispatch`):

1. **deploy-infra.yml** — provisions all Azure resources with Bicep.
   > ⚠️ The Developer-tier APIM service takes ~30–45 minutes to provision on first run.
2. **deploy-app.yml** — builds and zip-deploys the Function App.

### Deployment notes & environment constraints

These reflect choices validated against a Microsoft-internal sponsored (MCAP) subscription; adjust for your own:

- **OIDC via User-Assigned Managed Identity** — if your tenant blocks app registrations
  (`ServiceManagementReference` required), use a UAMI with a federated credential
  (subject `repo:<owner>/<repo>:environment:dev`) instead of an app registration. `azure/login`
  works with either.
- **Region** — defaults to `northeurope` (West Europe had Cosmos zonal capacity constraints at deploy time).
- **Function hosting** — uses **Flex Consumption (FC1)**. Consumption (Y1) and Dedicated (B1) plans require
  `Microsoft.Web` "Total VMs" quota, which is 0 on some sponsored subscriptions.
- **Keyless storage** — the storage account has `allowSharedKeyAccess=false` (often enforced by policy),
  so the Function uses identity-based storage (`AzureWebJobsStorage__blobServiceUri` + deployment storage
  via `SystemAssignedIdentity`). The Function MI is granted Storage Blob Data Owner + Queue Data Contributor.
- **APIM operations** — the API uses **explicit operations** (not a `/*` wildcard, which did not match at
  the gateway). `subscriptionRequired` is `false` for the management API.

### Local development

```bash
cd src/TeamSubscriptions.Functions
cp local.settings.json.sample local.settings.json   # fill in your values
func start
```

> Local runs use your developer identity via `DefaultAzureCredential` (`az login`), so the same
> RBAC assignments must apply to your user for Cosmos/APIM/Graph calls to succeed.
