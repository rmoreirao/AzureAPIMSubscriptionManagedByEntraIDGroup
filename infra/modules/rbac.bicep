@description('Principal (object) ID of the Function App system-assigned managed identity.')
param functionPrincipalId string

@description('Name of the existing API Management service.')
param apimName string

@description('Name of the existing Cosmos DB account.')
param cosmosAccountName string

// Built-in role: API Management Service Contributor (control plane).
var apimServiceContributorRoleId = '312a565d-c81f-4fd8-895a-4e21e48d571c'

// Built-in Cosmos DB SQL data-plane role: Cosmos DB Built-in Data Contributor.
var cosmosDataContributorRoleId = '00000000-0000-0000-0000-000000000002'

resource apim 'Microsoft.ApiManagement/service@2023-09-01-preview' existing = {
  name: apimName
}

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' existing = {
  name: cosmosAccountName
}

resource apimRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(apim.id, functionPrincipalId, apimServiceContributorRoleId)
  scope: apim
  properties: {
    principalId: functionPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', apimServiceContributorRoleId)
  }
}

resource cosmosDataRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: cosmos
  name: guid(cosmos.id, functionPrincipalId, cosmosDataContributorRoleId)
  properties: {
    principalId: functionPrincipalId
    roleDefinitionId: '${cosmos.id}/sqlRoleDefinitions/${cosmosDataContributorRoleId}'
    scope: cosmos.id
  }
}
