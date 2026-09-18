param location string
param tags object
param name string

@description('Principal allowed to pull images (the API managed identity).')
param pullPrincipalId string

var acrPullRoleId = '7f951dff-4ed5-43fd-a50b-7a2d9b8b3a3a'

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
  }
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, pullPrincipalId, acrPullRoleId)
  scope: registry
  properties: {
    principalId: pullPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
  }
}

output name string = registry.name
output loginServer string = registry.properties.loginServer
