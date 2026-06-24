targetScope = 'resourceGroup'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Short prefix for resource names (lowercase alphanumeric).')
@minLength(3)
@maxLength(11)
param prefix string = 'apimteam'

@description('Publisher email for API Management / developer portal.')
param publisherEmail string

@description('Publisher (organization) name for API Management.')
param publisherName string

@description('Entra ID audience APIM uses to call the Function App keylessly. Set after Easy Auth is configured; leave blank initially.')
param functionAuthResource string = ''

@description('Tags applied to all resources.')
param tags object = {
  workload: 'apim-team-subscriptions'
  managedBy: 'bicep'
}

var suffix = uniqueString(resourceGroup().id)
var logAnalyticsName = '${prefix}-log-${suffix}'
var appInsightsName = '${prefix}-ai-${suffix}'
var storageAccountName = toLower('${prefix}st${suffix}')
var cosmosAccountName = toLower('${prefix}-cosmos-${suffix}')
var planName = '${prefix}-plan-${suffix}'
var functionAppName = '${prefix}-func-${suffix}'
var apimName = '${prefix}-apim-${suffix}'

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    location: location
    logAnalyticsName: logAnalyticsName
    appInsightsName: appInsightsName
    tags: tags
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    storageAccountName: storageAccountName
    tags: tags
  }
}

module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmos'
  params: {
    location: location
    accountName: cosmosAccountName
    tags: tags
  }
}

module functionApp 'modules/functionApp.bicep' = {
  name: 'functionApp'
  params: {
    location: location
    planName: planName
    functionAppName: functionAppName
    storageAccountName: storage.outputs.storageAccountName
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    cosmosEndpoint: cosmos.outputs.documentEndpoint
    cosmosDatabaseName: cosmos.outputs.databaseName
    cosmosContainerName: cosmos.outputs.containerName
    apimServiceName: apimName
    tags: tags
  }
}

module apim 'modules/apim.bicep' = {
  name: 'apim'
  params: {
    location: location
    apimName: apimName
    publisherEmail: publisherEmail
    publisherName: publisherName
    functionAppHostName: functionApp.outputs.functionAppHostName
    functionAuthResource: functionAuthResource
    tags: tags
  }
}

module rbac 'modules/rbac.bicep' = {
  name: 'rbac'
  params: {
    functionPrincipalId: functionApp.outputs.functionAppPrincipalId
    apimName: apim.outputs.apimName
    cosmosAccountName: cosmos.outputs.accountName
  }
}

output functionAppName string = functionApp.outputs.functionAppName
output apimName string = apim.outputs.apimName
output apimGatewayUrl string = apim.outputs.gatewayUrl
output developerPortalUrl string = apim.outputs.developerPortalUrl
output cosmosEndpoint string = cosmos.outputs.documentEndpoint
output functionPrincipalId string = functionApp.outputs.functionAppPrincipalId
