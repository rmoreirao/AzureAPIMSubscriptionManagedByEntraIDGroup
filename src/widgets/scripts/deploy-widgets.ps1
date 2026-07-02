<#
.SYNOPSIS
  Deploys one or all Group Subscription custom widgets to an APIM Developer Portal.

.DESCRIPTION
  Exports the APIM service coordinates as environment variables (consumed by each widget's
  deploy.js) and runs `npm run deploy` for the requested widget(s). Values may be supplied as
  parameters or via the APIM_* environment variables; the subscription id falls back to the current
  `az account show` context when omitted.

.PARAMETER Widget
  Which widget to deploy: "all" (default) or one of cw-group-subscriptions,
  cw-custom-product-subscription.

.PARAMETER SubscriptionId
  Azure subscription id of the APIM service. Defaults to $env:APIM_SUBSCRIPTION_ID, then `az account show`.

.PARAMETER ResourceGroup
  Resource group containing the APIM service. Defaults to $env:APIM_RESOURCE_GROUP.

.PARAMETER ServiceName
  APIM service name. Defaults to $env:APIM_SERVICE_NAME.

.PARAMETER ManagementEndpoint
  ARM endpoint. Defaults to $env:APIM_MANAGEMENT_ENDPOINT or https://management.azure.com.

.PARAMETER ApiVersion
  APIM management API version. Defaults to $env:APIM_API_VERSION or 2024-05-01.

.PARAMETER AccessToken
  Optional bearer token. When omitted, a fresh token is minted via `az account get-access-token`
  (falling back to $env:AZ_ACCESS_TOKEN only when az is unavailable).

.PARAMETER DryRun
  Resolve and print the config (build still runs) without pushing anything to blob storage.

.PARAMETER SkipBuild
  Run `node deploy.js` directly instead of `npm run deploy` (skips the tsc + vite build).

.PARAMETER SkipPublish
  Do not publish (create a new portal revision of) the APIM developer portal after deploying widgets.

.EXAMPLE
  ./deploy-widgets.ps1 -ResourceGroup <rg> -ServiceName <apim-service>

.EXAMPLE
  ./deploy-widgets.ps1 -Widget cw-group-subscriptions -DryRun
#>
[CmdletBinding()]
param(
  [ValidateSet("all", "cw-group-subscriptions", "cw-custom-product-subscription")]
  [string]$Widget = "all",
  [string]$SubscriptionId = $env:APIM_SUBSCRIPTION_ID,
  [string]$ResourceGroup = $env:APIM_RESOURCE_GROUP,
  [string]$ServiceName = $env:APIM_SERVICE_NAME,
  [string]$ManagementEndpoint = $(if ($env:APIM_MANAGEMENT_ENDPOINT) { $env:APIM_MANAGEMENT_ENDPOINT } else { "https://management.azure.com" }),
  [string]$ApiVersion = $(if ($env:APIM_API_VERSION) { $env:APIM_API_VERSION } else { "2024-05-01" }),
  [string]$FunctionBaseUrl = $env:FUNCTION_BASE_URL,
  [string]$AccessToken,
  [switch]$DryRun,
  [switch]$SkipBuild,
  [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

# .../src/widgets (this script lives in .../src/widgets/scripts)
$widgetsRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent (Split-Path -Parent $widgetsRoot)

# Load the repo's .env (single source of truth) and fill in any values not supplied as parameters.
# The .env uses AZURE_* / FUNCTION_BASE_URL keys; map them onto the APIM_* coordinates this tooling
# expects. Explicit parameters and pre-set APIM_* env vars always win.
. (Join-Path $repoRoot "scripts/lib/DotEnv.ps1")
$dotenv = Import-DotEnv -SetEnv $false
if (-not $SubscriptionId)  { $SubscriptionId  = if ($dotenv["APIM_SUBSCRIPTION_ID"]) { $dotenv["APIM_SUBSCRIPTION_ID"] } else { $dotenv["AZURE_SUBSCRIPTION_ID"] } }
if (-not $ResourceGroup)   { $ResourceGroup   = if ($dotenv["APIM_RESOURCE_GROUP"])  { $dotenv["APIM_RESOURCE_GROUP"] }  else { $dotenv["AZURE_RESOURCE_GROUP"] } }
if (-not $ServiceName)     { $ServiceName     = $dotenv["APIM_SERVICE_NAME"] }
if (-not $FunctionBaseUrl) { $FunctionBaseUrl = $dotenv["FUNCTION_BASE_URL"] }

$allWidgets = @("cw-group-subscriptions", "cw-custom-product-subscription")
$targets = if ($Widget -eq "all") { $allWidgets } else { @($Widget) }

# Fall back to the current az subscription if none was provided.
if (-not $SubscriptionId -and (Get-Command az -ErrorAction SilentlyContinue)) {
  $azSub = (az account show --query id -o tsv 2>$null)
  if ($LASTEXITCODE -eq 0 -and $azSub) { $SubscriptionId = $azSub.Trim() }
}

# Resolve a bearer token. Prefer an explicitly supplied token; otherwise always mint a fresh one
# from az, because a cached $env:AZ_ACCESS_TOKEN from an earlier run is likely expired (which the
# widget tooling reports as the misleading "Could not get storage SAS URL"). Fall back to the env
# var only when az is unavailable.
if (-not $AccessToken -and (Get-Command az -ErrorAction SilentlyContinue)) {
  $azToken = (az account get-access-token --query accessToken -o tsv 2>$null)
  if ($LASTEXITCODE -eq 0 -and $azToken) { $AccessToken = $azToken.Trim() }
}
if (-not $AccessToken) { $AccessToken = $env:AZ_ACCESS_TOKEN }

$missing = @()
if (-not $SubscriptionId) { $missing += "SubscriptionId / APIM_SUBSCRIPTION_ID" }
if (-not $ResourceGroup)  { $missing += "ResourceGroup / APIM_RESOURCE_GROUP" }
if (-not $ServiceName)    { $missing += "ServiceName / APIM_SERVICE_NAME" }
if ($missing.Count -gt 0) {
  throw "Missing required value(s): $($missing -join ', '). Pass as parameters or set the env vars."
}

# Export the variables consumed by each widget's deploy.js.
$env:APIM_SUBSCRIPTION_ID = $SubscriptionId
$env:APIM_RESOURCE_GROUP = $ResourceGroup
$env:APIM_SERVICE_NAME = $ServiceName
$env:APIM_MANAGEMENT_ENDPOINT = $ManagementEndpoint
$env:APIM_API_VERSION = $ApiVersion
if ($AccessToken) { $env:AZ_ACCESS_TOKEN = $AccessToken }
if ($DryRun) { $env:WIDGET_DEPLOY_DRYRUN = "1" }
else { Remove-Item Env:WIDGET_DEPLOY_DRYRUN -ErrorAction SilentlyContinue }

Write-Host "APIM service : $ServiceName" -ForegroundColor Cyan
Write-Host "Resource group: $ResourceGroup" -ForegroundColor Cyan
Write-Host "Subscription : $SubscriptionId" -ForegroundColor Cyan
if ($FunctionBaseUrl) { Write-Host "Function URL : $FunctionBaseUrl" -ForegroundColor Cyan }
Write-Host "Widgets      : $($targets -join ', ')" -ForegroundColor Cyan
if ($DryRun) { Write-Host "DRY RUN - nothing will be pushed to blob storage." -ForegroundColor Yellow }

# A configured FUNCTION_BASE_URL is baked into each widget's compiled default so the deployed widget
# works without hand-editing the Dev Portal editor value. Placeholders containing '<' are ignored
# (the widget treats them as "not configured").
$injectBaseUrl = $FunctionBaseUrl -and ($FunctionBaseUrl -notmatch '[<>]')
function Set-WidgetFunctionBaseUrl {
  param([Parameter(Mandatory)][string]$ValuesPath, [Parameter(Mandatory)][string]$Url)
  $content = Get-Content -LiteralPath $ValuesPath -Raw
  $updated = [regex]::Replace($content, '(functionBaseUrl:\s*")[^"]*(")', "`${1}$Url`${2}")
  Set-Content -LiteralPath $ValuesPath -Value $updated -NoNewline
}

$failures = @()
foreach ($w in $targets) {
  $dir = Join-Path $widgetsRoot $w
  Write-Host "`n=== $w ===" -ForegroundColor Green
  Push-Location $dir
  $valuesPath = Join-Path $dir "src/values.ts"
  $originalValues = $null
  try {
    if ($injectBaseUrl -and (Test-Path $valuesPath)) {
      $originalValues = Get-Content -LiteralPath $valuesPath -Raw
      Set-WidgetFunctionBaseUrl -ValuesPath $valuesPath -Url $FunctionBaseUrl
      Write-Host "Injected functionBaseUrl into $w/src/values.ts" -ForegroundColor DarkGray
    }

    if (-not (Test-Path (Join-Path $dir "node_modules"))) {
      Write-Host "Installing dependencies..."
      npm install
      if ($LASTEXITCODE -ne 0) { throw "npm install failed for $w" }
    }

    if ($SkipBuild) { node deploy.js } else { npm run deploy }
    if ($LASTEXITCODE -ne 0) { throw "deploy failed for $w (exit $LASTEXITCODE)" }
  }
  catch {
    Write-Warning $_.Exception.Message
    $failures += $w
  }
  finally {
    # Restore the source placeholder so no environment-specific URL is left in the working tree.
    if ($null -ne $originalValues) { Set-Content -LiteralPath $valuesPath -Value $originalValues -NoNewline }
    Pop-Location
  }
}

if ($failures.Count -gt 0) {
  throw "Deployment failed for: $($failures -join ', ')"
}

Write-Host "`nAll requested widgets processed successfully." -ForegroundColor Green

# Publish the developer portal by creating a new portal revision so the deployed widgets go live.
if ($DryRun) {
  Write-Host "`nDRY RUN - skipping developer portal publish." -ForegroundColor Yellow
}
elseif ($SkipPublish) {
  Write-Host "`nSkipPublish set - developer portal was not published." -ForegroundColor Yellow
}
else {
  if (-not $AccessToken) {
    throw "Cannot publish developer portal: no access token available. Pass -AccessToken or sign in with 'az login'."
  }

  $revisionId = "deploy-widgets-{0}" -f (Get-Date -Format "yyyyMMddHHmmss")
  $publishUri = "{0}/subscriptions/{1}/resourceGroups/{2}/providers/Microsoft.ApiManagement/service/{3}/portalRevisions/{4}?api-version={5}" -f `
    $ManagementEndpoint.TrimEnd('/'), $SubscriptionId, $ResourceGroup, $ServiceName, $revisionId, $ApiVersion

  $publishBody = @{
    properties = @{
      description = "Published by deploy-widgets.ps1 on $(Get-Date -Format o)"
      isCurrent   = $true
    }
  } | ConvertTo-Json -Depth 5

  Write-Host "`nPublishing developer portal (revision $revisionId)..." -ForegroundColor Cyan
  try {
    Invoke-RestMethod -Method Put -Uri $publishUri -Body $publishBody `
      -Headers @{ Authorization = "Bearer $AccessToken" } -ContentType "application/json" | Out-Null
    Write-Host "Developer portal publish initiated successfully." -ForegroundColor Green
  }
  catch {
    throw "Developer portal publish failed: $($_.Exception.Message)"
  }
}
