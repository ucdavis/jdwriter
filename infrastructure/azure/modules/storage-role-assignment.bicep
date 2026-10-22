targetScope = 'resourceGroup'

@description('Storage account the role is granted on.')
param storageAccountName string

@description('Object ID of the principal receiving the role.')
param principalId string

@description('Fully qualified Azure RBAC role definition ID.')
param roleDefinitionId string

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource assignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(account.id, principalId, roleDefinitionId)
  scope: account
  properties: {
    roleDefinitionId: roleDefinitionId
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

output roleAssignmentId string = assignment.id
