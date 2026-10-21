param location string
param namePrefix string
param tenantId string
param apiPrincipalId string
param apiIdentityName string

var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-${take(namePrefix, 9)}-${take(uniqueString(resourceGroup().id), 8)}'
  location: location
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
  }
}

resource apiReadsSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, apiIdentityName, keyVaultSecretsUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output vaultName string = vault.name
output vaultUri string = vault.properties.vaultUri
