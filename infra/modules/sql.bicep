param location string
param tags object
param serverName string
param databaseName string
param adminGroupName string
param adminGroupObjectId string

resource server 'Microsoft.Sql/servers@2023-08-01' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: adminGroupName
      sid: adminGroupObjectId
      principalType: 'Group'
      tenantId: tenant().tenantId
    }
  }
}

// Container Apps on the consumption plan has no fixed outbound IP, so Azure-internal traffic is allowed.
// Only Entra identities can sign in (no SQL passwords exist), which is what actually protects the server.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: server
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: server
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    requestedBackupStorageRedundancy: 'Local'
  }
}

output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
