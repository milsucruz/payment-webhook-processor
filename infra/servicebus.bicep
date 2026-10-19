param location string
param namePrefix string
param apiPrincipalId string
param workerPrincipalId string
param apiIdentityName string
param workerIdentityName string

var senderRoleId = '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
var receiverRoleId = '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'

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
  name: guid(paymentEvents.id, apiIdentityName, senderRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', senderRoleId)
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource apiReceivesEvents 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: paymentEvents
  name: guid(paymentEvents.id, apiIdentityName, receiverRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', receiverRoleId)
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource apiReceivesNotifications 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: paymentNotifications
  name: guid(paymentNotifications.id, apiIdentityName, receiverRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', receiverRoleId)
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource workerReceivesEvents 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: paymentEvents
  name: guid(paymentEvents.id, workerIdentityName, receiverRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', receiverRoleId)
    principalId: workerPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource workerSendsNotifications 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: paymentNotifications
  name: guid(paymentNotifications.id, workerIdentityName, senderRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', senderRoleId)
    principalId: workerPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output namespaceName string = namespace.name
