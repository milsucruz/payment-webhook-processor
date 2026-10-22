param location string
param apiName string
param workerName string
param migratorName string

resource api 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: apiName
  location: location
}

resource worker 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: workerName
  location: location
}

resource migrator 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: migratorName
  location: location
}

output apiPrincipalId string = api.properties.principalId
output workerPrincipalId string = worker.properties.principalId
output migratorPrincipalId string = migrator.properties.principalId

output apiId string = api.id
output workerId string = worker.id
output migratorId string = migrator.id

output apiClientId string = api.properties.clientId
output workerClientId string = worker.properties.clientId
output migratorClientId string = migrator.properties.clientId
