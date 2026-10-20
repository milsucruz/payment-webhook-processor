targetScope = 'resourceGroup'

param location string = resourceGroup().location
@minLength(3)
@maxLength(24)
param namePrefix string
param sqlAdminLogin string
param sqlAdminObjectId string

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

module sql 'sql.bicep' = {
  name: 'sql'
  params: {
    location: location
    namePrefix: namePrefix
    adminLogin: sqlAdminLogin
    adminObjectId: sqlAdminObjectId
    tenantId: subscription().tenantId
  }
}
