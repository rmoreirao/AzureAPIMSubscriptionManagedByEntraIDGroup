@description('Azure region for the Function App.')
param location string

@description('Name of the Flex Consumption hosting plan.')
param planName string

@description('Name of the Function App.')
param functionAppName string

@description('Name of the existing storage account used by the Functions runtime.')
param storageAccountName string

@description('Application Insights connection string.')
param appInsightsConnectionString string

@description('Cosmos DB document endpoint (https://...).')
param cosmosEndpoint string

@description('Cosmos DB database name.')
param cosmosDatabaseName string

@description('Cosmos DB container name.')
param cosmosContainerName string

@description('Name of the APIM service the Function manages subscriptions on.')
param apimServiceName string

@description('Base URL of the APIM Developer Portal; used to authenticate Dev Portal requests and to allow CORS.')
param devPortalUrl string

@description('Tags applied to all resources.')
param tags object = {}

var deploymentContainerName = 'deploymentpackage'

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: '${storageAccountName}/default/${deploymentContainerName}'
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  tags: tags
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  kind: 'functionapp'
  properties: {
    reserved: true
  }
}

var storageBlobUri = storage.properties.primaryEndpoints.blob
var storageQueueUri = storage.properties.primaryEndpoints.queue
var storageTableUri = storage.properties.primaryEndpoints.table

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storageBlobUri}${deploymentContainerName}'
          authentication: {
            type: 'SystemAssignedIdentity'
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 40
        instanceMemoryMB: 2048
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '8.0'
      }
    }
    siteConfig: {
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'AzureWebJobsStorage__blobServiceUri'
          value: storageBlobUri
        }
        {
          name: 'AzureWebJobsStorage__queueServiceUri'
          value: storageQueueUri
        }
        {
          name: 'AzureWebJobsStorage__tableServiceUri'
          value: storageTableUri
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          name: 'Cosmos__Endpoint'
          value: cosmosEndpoint
        }
        {
          name: 'Cosmos__Database'
          value: cosmosDatabaseName
        }
        {
          name: 'Cosmos__Container'
          value: cosmosContainerName
        }
        {
          name: 'Apim__SubscriptionId'
          value: subscription().subscriptionId
        }
        {
          name: 'Apim__ResourceGroup'
          value: resourceGroup().name
        }
        {
          name: 'Apim__ServiceName'
          value: apimServiceName
        }
        {
          name: 'DevPortal__Url'
          value: devPortalUrl
        }
      ]
      cors: {
        // Custom widgets call the Functions directly from the Developer Portal origin.
        allowedOrigins: [
          devPortalUrl
        ]
        supportCredentials: false
      }
    }
  }
  dependsOn: [
    deploymentContainer
  ]
}

output functionAppName string = functionApp.name
output functionAppId string = functionApp.id
output functionAppPrincipalId string = functionApp.identity.principalId
output functionAppHostName string = functionApp.properties.defaultHostName
