targetScope = 'resourceGroup'

@description('Storage account that issues user-delegation keys.')
param storageAccountName string

@description('Object ID of the principal receiving the role.')
param principalId string

@description('Only this container can be read, written, or deleted by the principal.')
param containerName string = 'uploads'

@allowed([
  'ServicePrincipal'
  'User'
])
param principalType string = 'ServicePrincipal'

var delegatorRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'db58b8e5-c6ad-4a2a-8342-4190687cbf4a')
var contributorRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' existing = {
  parent: account
  name: 'default'
}

resource container 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' existing = {
  parent: blobService
  name: containerName
}

// Delegation keys require account scope. This role gives no access to blob contents.
resource delegation 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(account.id, principalId, delegatorRoleId)
  scope: account
  properties: {
    roleDefinitionId: delegatorRoleId
    principalId: principalId
    principalType: principalType
  }
}

resource dataAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(container.id, principalId, contributorRoleId)
  scope: container
  properties: {
    roleDefinitionId: contributorRoleId
    principalId: principalId
    principalType: principalType
  }
}

output delegationRoleAssignmentId string = delegation.id
output dataRoleAssignmentId string = dataAccess.id
