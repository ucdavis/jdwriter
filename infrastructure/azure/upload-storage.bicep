targetScope = 'resourceGroup'

@description('Expected Azure subscription ID. Must match the deployment subscription.')
param expectedSubscriptionId string

param appName string = 'jdwriter'

@allowed([
  'prod'
])
@description('One shared account in the production resource group, with separate uploads and test containers.')
param env string = 'prod'

param location string = resourceGroup().location

@description('Entra user object IDs to grant access to the test container. No application secrets or storage keys.')
param testUserPrincipalIds array = []

@description('Browser origins besides the existing App Service hostname. CORS is account-wide; RBAC separates the containers.')
param uploadCorsOrigins array = [
  'https://people.caes.ucdavis.edu'
  'http://localhost:5173'
  'http://127.0.0.1:5173'
]

var appNameSafe = toLower(replace(replace(appName, ' ', ''), '_', ''))
var nameToken = substring(uniqueString(resourceGroup().id, appName, env), 0, 6)
var storageAccountName = take(toLower('st${replace(appNameSafe, '-', '')}${env}${nameToken}'), 24)
var webAppName = toLower('web-${appNameSafe}-${env}-${nameToken}')
var deploymentGuardPassed = !empty(expectedSubscriptionId) && toLower(subscription().subscriptionId) == toLower(expectedSubscriptionId) && toLower(resourceGroup().name) == 'rg-${appNameSafe}-${env}'

resource webApp 'Microsoft.Web/sites@2023-12-01' existing = {
  name: webAppName
}

// Reuses the full deployment's module and names without modifying compute, SQL, or app settings.
module storage 'modules/storage.bicep' = if (deploymentGuardPassed) {
  name: 'upload-storage-${env}'
  params: {
    name: storageAccountName
    location: location
    tags: {
      application: appName
      environment: env
    }
    corsOrigins: union([
      'https://${webApp.properties.defaultHostName}'
    ], uploadCorsOrigins)
  }
}

module appAccess 'modules/storage-role-assignment.bicep' = if (deploymentGuardPassed) {
  name: 'upload-app-access-${env}'
  params: {
    storageAccountName: storage!.outputs.accountName
    containerName: storage!.outputs.containerName
    principalId: webApp.identity.principalId
  }
}

module testAccess 'modules/storage-role-assignment.bicep' = [for principalId in testUserPrincipalIds: if (deploymentGuardPassed) {
  name: 'upload-test-access-${uniqueString(principalId)}'
  params: {
    storageAccountName: storage!.outputs.accountName
    containerName: storage!.outputs.testContainerName
    principalId: principalId
    principalType: 'User'
  }
}]

output deploymentGuardPassed bool = deploymentGuardPassed
output uploadStorageAccountName string = deploymentGuardPassed ? storage!.outputs.accountName : ''
output uploadBlobEndpoint string = deploymentGuardPassed ? storage!.outputs.blobEndpoint : ''
output productionContainer string = deploymentGuardPassed ? storage!.outputs.containerName : ''
output testContainer string = deploymentGuardPassed ? storage!.outputs.testContainerName : ''
