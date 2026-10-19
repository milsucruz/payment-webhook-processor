param location string
param namePrefix string
param apiPrincipalId string
param workerPrincipalId string
param apiIdentityName string
param workerIdentityName string

var serviceBusDataSenderRoleId = '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
var serviceBusDataReceiverRoleId = '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'

resource namespace 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: 'sb-${namePrefix}-${uniqueString(resourceGroup().id)}'
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    disableLocalAuth: true
  }
}

resource paymentEvents 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: namespace
  name: 'payment-events'
  properties: {
    lockDuration: 'PT1M'
    maxDeliveryCount: 5
    defaultMessageTimeToLive: 'P14D'
    deadLetteringOnMessageExpiration: true
  }
}

resource paymentNotifications 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: namespace
  name: 'payment-notifications'
  properties: {
    lockDuration: 'PT1M'
    maxDeliveryCount: 5
    defaultMessageTimeToLive: 'PT1M'
    deadLetteringOnMessageExpiration: false
  }
}

resource apiSendsEvents 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: paymentEvents
  name: guid(paymentEvents.id, apiIdentityName, serviceBusDataSenderRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataSenderRoleId)
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource apiReceivesEvents 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: paymentEvents
  name: guid(paymentEvents.id, apiIdentityName, serviceBusDataReceiverRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataReceiverRoleId)
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource apiReceivesNotifications 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: paymentNotifications
  name: guid(paymentNotifications.id, apiIdentityName, serviceBusDataReceiverRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataReceiverRoleId)
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource workerReceivesEvents 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: paymentEvents
  name: guid(paymentEvents.id, workerIdentityName, serviceBusDataReceiverRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataReceiverRoleId)
    principalId: workerPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource workerSendsNotifications 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: paymentNotifications
  name: guid(paymentNotifications.id, workerIdentityName, serviceBusDataSenderRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataSenderRoleId)
    principalId: workerPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output namespaceName string = namespace.name
