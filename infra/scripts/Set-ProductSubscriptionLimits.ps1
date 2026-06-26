<#
.SYNOPSIS
    Raises the "Subscription count limit" (subscriptionsLimit) on every APIM product so that
    standalone (owner-less) group subscriptions are not blocked.

.DESCRIPTION
    Group subscriptions are created as standalone subscriptions (no owner). APIM treats every
    owner-less subscription as belonging to the same implicit user, so a product's
    "Subscription count limit" (subscriptionsLimit) caps the TOTAL number of standalone
    subscriptions for that product across all groups. With the default limit of 1, only one group
    can ever subscribe to a product, and additional attempts fail with:

        "Subscriptions limit reached for same user"

    This post-deployment script sets subscriptionsLimit to a high value (default 10000) on every
    product in the API Management instance, letting the application enforce its own per-group
    limits instead.

.PARAMETER ResourceGroup
    Resource group containing the API Management service.

.PARAMETER ApimName
    Name of the API Management service. When omitted, the single APIM instance in the resource
    group is used (fails if there are zero or multiple).

.PARAMETER Limit
    The subscription count limit to set on every product. Defaults to 10000.

.EXAMPLE
    ./Set-ProductSubscriptionLimits.ps1 -ResourceGroup rg-apimteam-dev

.EXAMPLE
    ./Set-ProductSubscriptionLimits.ps1 -ResourceGroup rg-apimteam-dev -ApimName apimteam-apim-xxxx -Limit 10000
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroup,

    [Parameter(Mandatory = $false)]
    [string]$ApimName,

    [Parameter(Mandatory = $false)]
    [int]$Limit = 10000
)

$ErrorActionPreference = 'Stop'

function Invoke-Az {
    param([string[]]$Arguments)
    $output = az @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "az $($Arguments -join ' ') failed: $output"
    }
    return $output
}

if (-not $ApimName) {
    Write-Host "Discovering API Management service in resource group '$ResourceGroup'..."
    $apimNames = Invoke-Az @('apim', 'list', '--resource-group', $ResourceGroup, '--query', '[].name', '-o', 'tsv')
    $apimList = @($apimNames -split "`r?`n" | Where-Object { $_ -ne '' })
    if ($apimList.Count -eq 0) {
        throw "No API Management service found in resource group '$ResourceGroup'."
    }
    if ($apimList.Count -gt 1) {
        throw "Multiple API Management services found in '$ResourceGroup' ($($apimList -join ', ')). Pass -ApimName to disambiguate."
    }
    $ApimName = $apimList[0]
}

Write-Host "Setting subscription count limit to $Limit on all products of '$ApimName' (resource group '$ResourceGroup')."

$productsJson = Invoke-Az @('apim', 'product', 'list', '--resource-group', $ResourceGroup, '--service-name', $ApimName, '-o', 'json')
$products = @(($productsJson | ConvertFrom-Json))

if ($products.Count -eq 0) {
    Write-Warning "No products found on '$ApimName'. Nothing to update."
    return
}

foreach ($product in $products) {
    $productId = $product.name
    $current = $product.subscriptionsLimit

    if ($current -eq $Limit) {
        Write-Host "  - $productId already at $Limit; skipping."
        continue
    }

    Invoke-Az @(
        'apim', 'product', 'update',
        '--resource-group', $ResourceGroup,
        '--service-name', $ApimName,
        '--product-id', $productId,
        '--subscriptions-limit', "$Limit",
        '-o', 'none'
    ) | Out-Null

    Write-Host "  - $productId updated ($current -> $Limit)."
}

Write-Host "Done. All products on '$ApimName' now allow up to $Limit subscriptions."
