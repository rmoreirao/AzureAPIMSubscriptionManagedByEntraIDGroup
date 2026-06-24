@description('Azure region for the API Management service.')
param location string

@description('Name of the API Management service.')
param apimName string

@description('Publisher email shown in the developer portal / notifications.')
param publisherEmail string

@description('Publisher (organization) name.')
param publisherName string

@description('Default host name of the backend Function App (e.g. myfunc.azurewebsites.net).')
param functionAppHostName string

@description('Entra ID resource/audience used by APIM managed identity to call the Function App. Set to the Function App registration Application ID URI / client id once Easy Auth is configured. Leave blank to skip token acquisition.')
param functionAuthResource string = ''

@description('Tags applied to the API Management service.')
param tags object = {}

resource apim 'Microsoft.ApiManagement/service@2023-09-01-preview' = {
  name: apimName
  location: location
  tags: tags
  sku: {
    name: 'Developer'
    capacity: 1
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    publisherEmail: publisherEmail
    publisherName: publisherName
    // Developer Portal is available by default on the Developer tier.
  }
}

resource backend 'Microsoft.ApiManagement/service/backends@2023-09-01-preview' = {
  parent: apim
  name: 'team-subscriptions-functions'
  properties: {
    protocol: 'http'
    url: 'https://${functionAppHostName}/api'
  }
}

resource api 'Microsoft.ApiManagement/service/apis@2023-09-01-preview' = {
  parent: apim
  name: 'team-subscriptions'
  properties: {
    displayName: 'Team Subscriptions'
    path: 'team-subscriptions'
    protocols: [
      'https'
    ]
    subscriptionRequired: false
    apiType: 'http'
  }
}

resource wildcardOperation 'Microsoft.ApiManagement/service/apis/operations@2023-09-01-preview' = {
  parent: api
  name: 'all'
  properties: {
    displayName: 'All operations'
    method: '*'
    urlTemplate: '/*'
  }
}

var managedIdentitySnippet = empty(functionAuthResource)
  ? ''
  : '<authentication-managed-identity resource="${functionAuthResource}" />'

resource apiPolicy 'Microsoft.ApiManagement/service/apis/policies@2023-09-01-preview' = {
  parent: api
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: '<policies><inbound><base />${managedIdentitySnippet}<set-backend-service backend-id="${backend.name}" /></inbound><backend><base /></backend><outbound><base /></outbound><on-error><base /></on-error></policies>'
  }
  dependsOn: [
    wildcardOperation
  ]
}

output apimName string = apim.name
output apimId string = apim.id
output apimPrincipalId string = apim.identity.principalId
output gatewayUrl string = apim.properties.gatewayUrl
output developerPortalUrl string = apim.properties.developerPortalUrl
