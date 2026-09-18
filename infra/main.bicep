targetScope = 'resourceGroup'

@description('Azure region for all regional resources. Defaults to the resource group location.')
param location string = resourceGroup().location

@description('Short app name used in resource names.')
param appName string = 'expenses'

@description('Environment name used in resource names and tags.')
param environmentName string = 'prod'

@description('Object ID of the Entra group that administers the SQL server (created by bootstrap.ps1).')
param sqlAdminGroupObjectId string

@description('Display name of the SQL admin group.')
param sqlAdminGroupName string = 'Expenses SQL Admins'

@description('API container image. The placeholder is replaced by the deploy workflow; infra.yml passes the current image so re-running infra does not roll it back.')
param apiImage string = 'mcr.microsoft.com/dotnet/samples:aspnetapp'

@description('F0 is free (500 pages/month) but only one F0 Document Intelligence resource is allowed per subscription.')
@allowed([
  'F0'
  'S0'
])
param documentIntelligenceSku string = 'F0'

var suffix = uniqueString(resourceGroup().id)
var tags = {
  app: appName
  environment: environmentName
}

resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${appName}-api'
  location: location
  tags: tags
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    location: location
    tags: tags
    workspaceName: 'log-${appName}-${environmentName}'
    appInsightsName: 'appi-${appName}-${environmentName}'
  }
}

module registry 'modules/registry.bicep' = {
  name: 'registry'
  params: {
    location: location
    tags: tags
    name: 'cr${appName}${suffix}'
    pullPrincipalId: apiIdentity.properties.principalId
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    tags: tags
    name: 'st${appName}${suffix}'
    dataContributorPrincipalId: apiIdentity.properties.principalId
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    location: location
    tags: tags
    serverName: 'sql-${appName}-${suffix}'
    databaseName: 'sqldb-${appName}'
    adminGroupName: sqlAdminGroupName
    adminGroupObjectId: sqlAdminGroupObjectId
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyVault'
  params: {
    location: location
    tags: tags
    name: 'kv-exp-${suffix}'
    readerPrincipalId: apiIdentity.properties.principalId
  }
}

module documentIntelligence 'modules/documentIntelligence.bicep' = {
  name: 'documentIntelligence'
  params: {
    location: location
    tags: tags
    name: 'di-${appName}-${suffix}'
    sku: documentIntelligenceSku
    userPrincipalId: apiIdentity.properties.principalId
  }
}

module email 'modules/email.bicep' = {
  name: 'email'
  params: {
    tags: tags
    emailServiceName: 'ecs-${appName}-${environmentName}'
    communicationServiceName: 'acs-${appName}-${suffix}'
  }
}

module web 'modules/staticWebApp.bicep' = {
  name: 'web'
  params: {
    location: location
    tags: tags
    name: 'swa-${appName}-${environmentName}'
  }
}

module api 'modules/containerApp.bicep' = {
  name: 'api'
  params: {
    location: location
    tags: tags
    environmentName: 'cae-${appName}-${environmentName}'
    appName: 'ca-${appName}-api'
    image: apiImage
    identityId: apiIdentity.id
    registryLoginServer: registry.outputs.loginServer
    workspaceName: monitoring.outputs.workspaceName
    env: [
      { name: 'AZURE_CLIENT_ID', value: apiIdentity.properties.clientId }
      { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: monitoring.outputs.appInsightsConnectionString }
      {
        name: 'ConnectionStrings__Expenses'
        value: 'Server=tcp:${sql.outputs.serverFqdn},1433;Database=${sql.outputs.databaseName};Authentication=Active Directory Managed Identity;User Id=${apiIdentity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
      }
      { name: 'Cors__AllowedOrigins__0', value: 'https://${web.outputs.defaultHostname}' }
      { name: 'Storage__BlobEndpoint', value: storage.outputs.blobEndpoint }
      { name: 'DocumentIntelligence__Endpoint', value: documentIntelligence.outputs.endpoint }
      { name: 'Email__Endpoint', value: email.outputs.endpoint }
      { name: 'Email__SenderAddress', value: email.outputs.senderAddress }
      { name: 'KeyVault__Uri', value: keyVault.outputs.uri }
    ]
  }
}

output apiUrl string = 'https://${api.outputs.fqdn}'
output webUrl string = 'https://${web.outputs.defaultHostname}'
output containerAppName string = api.outputs.name
output staticWebAppName string = web.outputs.name
output registryLoginServer string = registry.outputs.loginServer
output registryName string = registry.outputs.name
output sqlServerFqdn string = sql.outputs.serverFqdn
output sqlDatabaseName string = sql.outputs.databaseName
output apiIdentityName string = apiIdentity.name
output apiIdentityClientId string = apiIdentity.properties.clientId
