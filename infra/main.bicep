targetScope = 'resourceGroup'

param location string = resourceGroup().location
param namePrefix string

var apiIdentityName = 'id-${namePrefix}-api'
var workerIdentityName = 'id-${namePrefix}-worker'
var migratorIdentityName = 'id-${namePrefix}-migrator'

module identity 'identity.bicep' = {
  name: 'identity'
  params: {
    location: location
    apiName: apiIdentityName
    workerName: workerIdentityName
    migratorName: migratorIdentityName
  }
}

module serviceBus 'servicebus.bicep' = {
  name: 'servicebus'
  params: {
    location: location
    namePrefix: namePrefix
    apiPrincipalId: identity.outputs.apiPrincipalId
    workerPrincipalId: identity.outputs.workerPrincipalId
    apiIdentityName: apiIdentityName
    workerIdentityName: workerIdentityName
  }
}
