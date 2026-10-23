@description('Storage account name: 3-24 lowercase letters and digits.')
param name string

@description('Azure region.')
param location string

@description('Tags to apply.')
param tags object

@description('Browser origins allowed to upload: the app\'s own address and any front door that serves it.')
param corsOrigins array

@description('Private container uploads wait in between the browser and the app.')
param containerName string = 'uploads'

@description('Private container for scrubbed development uploads in this same account.')
param testContainerName string = 'test'

// Upload staging. Browsers put files here with a short-lived, write-only link for a single blob
// (a user-delegation SAS the app signs with its managed identity), so uploads never pass through
// the front door's web application firewall. The app reads each file and deletes it at once; the
// lifecycle rule below removes anything abandoned.
resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    // Entra ID only: no account keys to leak. Upload links are user-delegation SAS.
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    // Browsers upload directly, so the endpoint is public; every request still needs a valid link.
    publicNetworkAccess: 'Enabled'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    // Staging is disposable. Deleted files must not remain in versions or soft-delete storage.
    isVersioningEnabled: false
    deleteRetentionPolicy: {
      enabled: false
    }
    containerDeleteRetentionPolicy: {
      enabled: false
    }
    changeFeed: {
      enabled: false
    }
    cors: {
      corsRules: [
        {
          allowedOrigins: corsOrigins
          allowedMethods: [
            'PUT'
            'OPTIONS'
          ]
          allowedHeaders: [
            'content-type'
            'x-ms-*'
          ]
          exposedHeaders: [
            'x-ms-request-id'
          ]
          maxAgeInSeconds: 300
        }
      ]
    }
  }
}

resource container 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: containerName
  properties: {
    publicAccess: 'None'
  }
}

resource testContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: testContainerName
  properties: {
    publicAccess: 'None'
  }
}

// Files become eligible after one day. Azure runs cleanup asynchronously, so this is not a
// 24-hour retention guarantee. The application must delete files immediately after reading.
resource lifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'delete-abandoned-uploads'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: {
              blobTypes: [
                'blockBlob'
              ]
              prefixMatch: [
                '${containerName}/'
                '${testContainerName}/'
              ]
            }
            actions: {
              baseBlob: {
                delete: {
                  daysAfterModificationGreaterThan: 1
                }
              }
            }
          }
        }
      ]
    }
  }
}

output accountName string = account.name
output blobEndpoint string = account.properties.primaryEndpoints.blob
output containerName string = container.name
output testContainerName string = testContainer.name
