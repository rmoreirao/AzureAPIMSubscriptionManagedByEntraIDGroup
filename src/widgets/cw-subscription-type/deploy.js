const {deployNodeJS} = require("@azure/api-management-custom-widgets-tools")

// Widget identifier (must match static/config.msapim.json "name").
const name = "cw-subscription-type"
const fallbackConfigPath = "./static/config.msapim.json"

// All APIM service coordinates come from environment variables, so no subscription ids or
// resource ids are committed to source control. Set them directly or, more conveniently, via
// src/widgets/scripts/deploy-widgets.ps1 which exports them before calling this script.
//
//   APIM_SUBSCRIPTION_ID      (required) Azure subscription id of the APIM service
//   APIM_RESOURCE_GROUP       (required) resource group containing the APIM service
//   APIM_SERVICE_NAME         (required) APIM service name
//   APIM_MANAGEMENT_ENDPOINT  (optional) ARM endpoint, default https://management.azure.com
//   APIM_API_VERSION          (optional) management API version, default 2024-05-01
//   AZ_ACCESS_TOKEN           (optional) bearer token; when unset an interactive sign-in is used
//   WIDGET_DEPLOY_DRYRUN      (optional) when set, print the resolved config and exit without deploying
const subscriptionId = process.env.APIM_SUBSCRIPTION_ID
const resourceGroup = process.env.APIM_RESOURCE_GROUP
const serviceName = process.env.APIM_SERVICE_NAME
const managementApiEndpoint = process.env.APIM_MANAGEMENT_ENDPOINT || "https://management.azure.com"
const apiVersion = process.env.APIM_API_VERSION || "2024-05-01"
const accessToken = process.env.AZ_ACCESS_TOKEN || null

const missing = [
  ["APIM_SUBSCRIPTION_ID", subscriptionId],
  ["APIM_RESOURCE_GROUP", resourceGroup],
  ["APIM_SERVICE_NAME", serviceName],
]
  .filter(([, value]) => !value)
  .map(([key]) => key)

if (missing.length > 0) {
  console.error(`Missing required environment variable(s): ${missing.join(", ")}.`)
  console.error("Set them or run via src/widgets/scripts/deploy-widgets.ps1 before deploying.")
  process.exit(1)
}

const serviceInformation = {
  resourceId: `subscriptions/${subscriptionId}/resourceGroups/${resourceGroup}/providers/Microsoft.ApiManagement/service/${serviceName}`,
  managementApiEndpoint,
  tokenOverride: accessToken ? `Bearer ${accessToken}` : null,
  apiVersion,
}

if (process.env.WIDGET_DEPLOY_DRYRUN) {
  console.log(`[dry-run] ${name} ->`, {
    ...serviceInformation,
    tokenOverride: serviceInformation.tokenOverride ? "Bearer <redacted>" : null,
  })
} else {
  deployNodeJS(serviceInformation, name, fallbackConfigPath)
}