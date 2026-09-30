param location string
param tags object
param name string

@description('Principal that wraps/unwraps the data-protection key (the API managed identity).')
param cryptoPrincipalId string

var cryptoUserRoleId = '12338af0-0e69-4776-bea7-57ae8d297424' // Key Vault Crypto User

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

// Encrypts the ASP.NET Core Data Protection keys that protect API session cookies. The API identity
// only wraps/unwraps with it (Key Vault Crypto User); the key material never leaves the vault.
resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: vault
  name: 'dataprotection'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: [
      'wrapKey'
      'unwrapKey'
    ]
  }
}

resource cryptoUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, cryptoPrincipalId, cryptoUserRoleId)
  scope: vault
  properties: {
    principalId: cryptoPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cryptoUserRoleId)
  }
}

output uri string = vault.properties.vaultUri

// Versionless key id, so key rotation does not require a redeploy.
output dataProtectionKeyId string = dataProtectionKey.properties.keyUri
