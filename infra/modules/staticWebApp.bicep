@description('Free tier is only offered in centralus, eastus2, westus2, westeurope, and eastasia.')
param location string
param tags object
param name string

resource site 'Microsoft.Web/staticSites@2024-04-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    // No pull-request preview environments: there is no staging, and previews would call the production API.
    stagingEnvironmentPolicy: 'Disabled'
    allowConfigFileUpdates: true
  }
}

output name string = site.name
output defaultHostname string = site.properties.defaultHostname
