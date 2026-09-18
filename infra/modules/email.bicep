param tags object
param emailServiceName string
param communicationServiceName string

@description('Where email data is stored at rest.')
param dataLocation string = 'United States'

resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: emailServiceName
  location: 'global'
  tags: tags
  properties: {
    dataLocation: dataLocation
  }
}

// Azure-managed domain: free, no DNS setup, sends from DoNotReply@<guid>.azurecomm.net.
resource managedDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  tags: tags
  properties: {
    domainManagement: 'AzureManaged'
    userEngagementTracking: 'Disabled'
  }
}

resource communicationService 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: communicationServiceName
  location: 'global'
  tags: tags
  properties: {
    dataLocation: dataLocation
    linkedDomains: [
      managedDomain.id
    ]
  }
}

output endpoint string = 'https://${communicationService.properties.hostName}'
output senderAddress string = 'DoNotReply@${managedDomain.properties.mailFromSenderDomain}'
