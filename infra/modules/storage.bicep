@description('Azure region for the storage account.')
param location string

@description('Globally-unique storage account name (3-24 lowercase alphanumeric).')
param storageAccountName string

@description('Tags applied to the storage account.')
param tags object = {}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    // The Flex Consumption Function App reaches this account over its public endpoint (identity-based,
    // no VNet/private endpoint), so public network access must stay enabled. A subscription policy in
    // this tenant periodically flips this to 'Disabled', which stops the Functions host from reading
    // its deployment package and returns HTTP 503 (surfaced in the browser as a CORS error). Setting
    // it explicitly here re-asserts public access on every deploy.
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

output storageAccountName string = storage.name
output storageAccountId string = storage.id
