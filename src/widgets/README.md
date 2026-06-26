# Group Subscription custom widgets

Azure API Management **Developer Portal custom widgets** (vanilla TypeScript, scaffolded with
[`@azure/api-management-custom-widgets-tools`](https://aka.ms/apimdocs/portal/customwidgets)) that
drive the group-subscription flows. They call the [`GroupSubscriptions.Functions`](../GroupSubscriptions.Functions)
backend **directly**, forwarding the logged-in user's APIM delegation SAS token and `xmh-*` context
headers so the Functions can confirm the request comes from a valid Dev Portal user.

| Widget | Folder | Flow | Backend calls |
|--------|--------|------|---------------|
| **Custom Product Subscription** | `cw-custom-product-subscription` | All-in-one widget: shows an **Active subscriptions** table (the caller's **user *and* group** subscriptions, columns **Type / Group / Name**), then a *User Subscription* / *Group Subscription* chooser that swaps (via JS, no redirects) to either the user create form or the group-subscription create form. A *Back* link returns to the chooser, and the table refreshes after a successful create. | `GET`/`POST /user-subscriptions`, `GET /apim/group-subscriptions`, `GET /apim/users/{userId}/groups`, `POST /apim/group-subscriptions` |
| **Subscription Type** | `cw-subscription-type` | Lets the user choose *User Subscription* vs *Group Subscription* and navigates accordingly. | none (navigation only) |
| **Subscriptions** | `cw-group-subscriptions` | Single table listing the caller's **user *and* group** subscriptions (Name, Type, Group, Product, State, Primary/Secondary Key, Date created) with a per-row "⋯" menu (*Show keys*, *Regenerate keys*, *Cancel subscription*). Inactive subscriptions show "The subscription is not active" instead of keys. | `GET /apim/group-subscriptions`, `GET /user-subscriptions`, `POST /apim/group-subscriptions/{group}/{subId}/{regenerate\|cancel}`, `POST /user-subscriptions/{subId}/{regenerate\|cancel}` |

> The group-subscription widgets call the **APIM-group** endpoints (`/apim/...`), which resolve group
> membership from the APIM Groups rather than Entra ID (the Dev Portal is not yet federated with Entra
> ID). The equivalent Entra-ID endpoints (`/users/{userId}/groups`, `/group-subscriptions`) still exist
> on the backend and are unchanged.

> **`cw-custom-product-subscription`** merges the navigation-only `cw-subscription-type` and the
> former standalone group-subscription create flow into a single widget that shows/hides its panels
> with JavaScript instead of redirecting between pages. `cw-subscription-type` is kept for
> backwards compatibility.

## How requests are authenticated

Each widget builds an authenticated `fetch` (`src/api.ts`) that injects, from the Dev Portal
`secrets` + `parentLocation`:

```
Authorization: <SAS token>          // secrets.token
xmh-userId:    <user id>            // secrets.userId
xmh-managementApiUrl: <mgmt url>    // secrets.managementApiUrl
xmh-apiVersion:       <version>     // secrets.apiVersion
xmh-origin:           <origin>      // secrets.parentLocation.origin
xmh-hostName:         <hostname>    // secrets.parentLocation.hostname
```

The Functions validate these headers (see the backend
[`Security`](../GroupSubscriptions.Functions/Security) folder and the
[README auth section](../../README.md#dev-portal-authentication)).

## Configuration (widget editor)

Each widget exposes editor values configured in the Dev Portal admin:

- **Custom Product Subscription** — chooser title, the two card labels/descriptions, the user/group
  panel titles, `functionBaseUrl`, and `scope`. The subscription scope is auto-derived from the
  hosting product page (`/product#product={id}` → `/products/{id}`); `scope` is only a fallback used
  when the widget is not on a product page (default `/apis` = all APIs).
- **Subscription Type** — labels, descriptions and target URLs for the two options.
- **Create Group Subscription** — `functionBaseUrl`, `scope` (APIM scope, default `/apis`),
  `successRedirectUrl`, and title.
- **Group Subscriptions** — `functionBaseUrl` and title.

`functionBaseUrl` is the deployed Function App API base, e.g.
`https://apimteam-func-3dexfwdm3jz34.azurewebsites.net/api`. The Function App must allow the Dev Portal origin in
its CORS settings (configured automatically by the Bicep infra).

## Develop & deploy

Each widget is an independent npm package:

```bash
cd src/widgets/<widget>
npm install
npm run build          # tsc + vite → ./dist
```

### Deploying

APIM service coordinates are read from environment variables (no ids are committed). The easiest
path is the helper script, which exports the variables and runs `npm run deploy` for one or all
widgets:

```powershell
# from src/widgets/scripts
./deploy-widgets.ps1 -ResourceGroup <rg> -ServiceName <apim-service> [-SubscriptionId <sub>]

# a single widget
./deploy-widgets.ps1 -Widget cw-group-subscriptions -ResourceGroup <rg> -ServiceName <apim-service>

# preview the resolved config without pushing anything
./deploy-widgets.ps1 -DryRun -ResourceGroup <rg> -ServiceName <apim-service>
```

`-SubscriptionId` falls back to `$env:APIM_SUBSCRIPTION_ID` and then to the current `az account show`
context. Authentication uses an interactive sign-in unless `-AccessToken` / `$env:AZ_ACCESS_TOKEN`
is provided.

To deploy a single widget manually instead of via the script, set the variables yourself and run
`npm run deploy`:

| Variable | Required | Default | Purpose |
|----------|----------|---------|---------|
| `APIM_SUBSCRIPTION_ID` | yes | – | Azure subscription id of the APIM service |
| `APIM_RESOURCE_GROUP` | yes | – | Resource group of the APIM service |
| `APIM_SERVICE_NAME` | yes | – | APIM service name |
| `APIM_MANAGEMENT_ENDPOINT` | no | `https://management.azure.com` | ARM endpoint |
| `APIM_API_VERSION` | no | `2024-05-01` | Management API version |
| `AZ_ACCESS_TOKEN` | no | – | Bearer token; interactive sign-in when unset |
| `WIDGET_DEPLOY_DRYRUN` | no | – | Print resolved config and skip the actual deploy |

After deploying, add each widget to a Dev Portal page from the portal admin and set its editor
values (notably `functionBaseUrl`).
