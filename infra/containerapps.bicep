param location string
param namePrefix string
param imageRegistry string
param imageTag string
param apiIdentityId string
param workerIdentityId string
param migratorIdentityId string
param vaultUri string
param workspaceName string
param faultInjectionEnabled bool

var placeholderServiceBusConnectionString = 'Endpoint=sb://placeholder.servicebus.windows.net/;SharedAccessKeyName=placeholder;SharedAccessKey=placeholder'

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: workspaceName
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
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

resource api 'Microsoft.App/containerApps@2024-03-01' = {
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
          image: '${imageRegistry}-api:${imageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'Stripe__WebhookSecret', secretRef: 'stripe-webhook-secret' }
            { name: 'Admin__ApiKey', secretRef: 'admin-api-key' }
            { name: 'ServiceBus__EventsQueueName', value: 'payment-events' }
            { name: 'ServiceBus__ConnectionString', value: placeholderServiceBusConnectionString }
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

resource worker 'Microsoft.App/containerApps@2024-03-01' = {
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
          image: '${imageRegistry}-worker:${imageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'ServiceBus__EventsQueueName', value: 'payment-events' }
            { name: 'ServiceBus__ConnectionString', value: placeholderServiceBusConnectionString }
            { name: 'FaultInjection__Enabled', value: string(faultInjectionEnabled) }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
      }
    }
  }
}

resource migrationJob 'Microsoft.App/jobs@2024-03-01' = {
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
          image: '${imageRegistry}-migrator:${imageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
    }
  }
}

output apiFqdn string = api.properties.configuration.ingress.fqdn
