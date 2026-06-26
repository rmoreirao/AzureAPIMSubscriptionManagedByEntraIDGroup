@description('Azure region for the Cosmos DB account.')
param location string

@description('Globally-unique Cosmos DB account name (lowercase).')
param accountName string

@description('Name of the SQL database.')
param databaseName string = 'groupsubscriptions'

@description('Name of the container storing group subscriptions.')
param containerName string = 'subscriptions'

@description('Tags applied to the Cosmos DB account.')
param tags object = {}

resource account 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: accountName
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    // Keyless: disable account keys, force Entra ID (RBAC) data-plane auth.
    disableLocalAuth: true
    enableFreeTier: false
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: account
  name: databaseName
  properties: {
    resource: {
      id: databaseName
    }
  }
}

resource container 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: containerName
  properties: {
    resource: {
      id: containerName
      partitionKey: {
        paths: [
          '/entraIdGroup'
        ]
        kind: 'Hash'
      }
    }
  }
}

output accountName string = account.name
output accountId string = account.id
output documentEndpoint string = account.properties.documentEndpoint
output databaseName string = databaseName
output containerName string = containerName
