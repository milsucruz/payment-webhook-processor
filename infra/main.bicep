targetScope = 'resourceGroup'

param location string = resourceGroup().location
@minLength(3)
@maxLength(24)
param namePrefix string
param sqlAdminLogin string
param sqlAdminObjectId string
param imageRegistry string
param apiImageTag string
param workerImageTag string
param migratorImageTag string
param faultInjectionEnabled bool
@secure()
param alertEmail string

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

module keyVault 'keyvault.bicep' = {
  name: 'keyvault'
  params: {
    location: location
    namePrefix: namePrefix
    tenantId: subscription().tenantId
    apiPrincipalId: identity.outputs.apiPrincipalId
    apiIdentityName: apiIdentityName
  }
}

module monitoring 'monitoring.bicep' = {
  name: 'monitoring'
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

module containerApps 'containerapps.bicep' = {
  name: 'containerapps'
  params: {
    location: location
    namePrefix: namePrefix
    imageRegistry: imageRegistry
    apiImageTag: apiImageTag
    workerImageTag: workerImageTag
    migratorImageTag: migratorImageTag
    apiIdentityId: identity.outputs.apiId
    workerIdentityId: identity.outputs.workerId
    migratorIdentityId: identity.outputs.migratorId
    apiClientId: identity.outputs.apiClientId
    workerClientId: identity.outputs.workerClientId
    migratorClientId: identity.outputs.migratorClientId
    serviceBusNamespace: serviceBus.outputs.fullyQualifiedNamespace
    serviceBusNamespaceName: serviceBus.outputs.namespaceName
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDatabaseName: sql.outputs.databaseName
    vaultUri: keyVault.outputs.vaultUri
    workspaceName: monitoring.outputs.workspaceName
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    faultInjectionEnabled: faultInjectionEnabled
  }
}

module alerts 'alerts.bicep' = {
  name: 'alerts'
  params: {
    namePrefix: namePrefix
    alertEmail: alertEmail
    serviceBusNamespaceName: serviceBus.outputs.namespaceName
    apiAppName: containerApps.outputs.apiName
  }
}

output apiFqdn string = containerApps.outputs.apiFqdn
