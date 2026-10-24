param location string
param namePrefix string
param imageRegistry string
param apiImageTag string
param workerImageTag string
param migratorImageTag string
param apiIdentityId string
param workerIdentityId string
param migratorIdentityId string
param apiClientId string
param workerClientId string
param migratorClientId string
param serviceBusNamespace string
param serviceBusNamespaceName string
param sqlServerFqdn string
param sqlDatabaseName string
param vaultUri string
param workspaceName string
param appInsightsConnectionString string
param faultInjectionEnabled bool

var paymentsConnectionString = 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Default;Encrypt=True'

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: workspaceName
}

resource environment 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: 'cae-${namePrefix}'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
  }
}

resource api 'Microsoft.App/containerApps@2025-01-01' = {
  name: 'ca-${namePrefix}-api'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        allowInsecure: false
      }
      secrets: [
        {
          name: 'stripe-webhook-secret'
          keyVaultUrl: '${vaultUri}secrets/stripe-webhook-secret'
          identity: apiIdentityId
        }
        {
          name: 'admin-api-key'
          keyVaultUrl: '${vaultUri}secrets/admin-api-key'
          identity: apiIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${imageRegistry}-api:${apiImageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'Stripe__WebhookSecret', secretRef: 'stripe-webhook-secret' }
            { name: 'Admin__ApiKey', secretRef: 'admin-api-key' }
            { name: 'ServiceBus__EventsQueueName', value: 'payment-events' }
            { name: 'ServiceBus__NotificationsQueueName', value: 'payment-notifications' }
            { name: 'ServiceBus__FullyQualifiedNamespace', value: serviceBusNamespace }
            { name: 'ConnectionStrings__Payments', value: paymentsConnectionString }
            { name: 'AZURE_CLIENT_ID', value: apiClientId }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
              }
              periodSeconds: 10
              timeoutSeconds: 3
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
              }
              initialDelaySeconds: 1
              periodSeconds: 5
              timeoutSeconds: 10
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
      }
    }
  }
}

resource worker 'Microsoft.App/containerApps@2025-01-01' = {
  name: 'ca-${namePrefix}-worker'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${workerIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: '${imageRegistry}-worker:${workerImageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'ServiceBus__EventsQueueName', value: 'payment-events' }
            { name: 'ServiceBus__NotificationsQueueName', value: 'payment-notifications' }
            { name: 'ServiceBus__FullyQualifiedNamespace', value: serviceBusNamespace }
            { name: 'AZURE_CLIENT_ID', value: workerClientId }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
            { name: 'ConnectionStrings__Payments', value: paymentsConnectionString }
            { name: 'FaultInjection__Enabled', value: string(faultInjectionEnabled) }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
        rules: [
          {
            name: 'payment-events-length'
            custom: {
              type: 'azure-servicebus'
              metadata: {
                namespace: serviceBusNamespaceName
                queueName: 'payment-events'
                messageCount: '20'
              }
              identity: workerIdentityId
            }
          }
        ]
      }
    }
  }
}

resource migrationJob 'Microsoft.App/jobs@2025-01-01' = {
  name: 'caj-${namePrefix}-migrator'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${migratorIdentityId}': {}
    }
  }
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 600
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
    }
    template: {
      containers: [
        {
          name: 'migrator'
          image: '${imageRegistry}-migrator:${migratorImageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'AZURE_CLIENT_ID', value: migratorClientId }
            { name: 'ConnectionStrings__Payments', value: paymentsConnectionString }
          ]
        }
      ]
    }
  }
}

output apiFqdn string = api.properties.configuration.ingress.fqdn
output apiName string = api.name
