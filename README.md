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

After the first infra deploy, grant the Function App's managed identity the Graph **application**
permissions `GroupMember.Read.All` and `User.Read.All`, then **admin-consent** them. This step
requires a tenant admin and cannot be done purely in Bicep.

## Deploy

Deployment is split into two workflows, both triggered on push to `main` (or via
`workflow_dispatch`):

1. **deploy-infra.yml** — provisions all Azure resources with Bicep.
   > ⚠️ The Developer-tier APIM service takes ~30–45 minutes to provision on first run.
2. **deploy-app.yml** — builds and zip-deploys the Function App.

### Local development

```bash
cd src/TeamSubscriptions.Functions
cp local.settings.json.sample local.settings.json   # fill in your values
func start
```

> Local runs use your developer identity via `DefaultAzureCredential` (`az login`), so the same
> RBAC assignments must apply to your user for Cosmos/APIM/Graph calls to succeed.
