using 'main.bicep'

param namePrefix = 'payment-webhooks'
param sqlAdminLogin = readEnvironmentVariable('SQL_ADMIN_LOGIN')
param sqlAdminObjectId = readEnvironmentVariable('SQL_ADMIN_OBJECT_ID')
param imageRegistry = 'ghcr.io/milsucruz/payment-webhook-processor'
param apiImageTag = readEnvironmentVariable('API_IMAGE_TAG')
param workerImageTag = readEnvironmentVariable('WORKER_IMAGE_TAG')
param migratorImageTag = readEnvironmentVariable('MIGRATOR_IMAGE_TAG')
param faultInjectionEnabled = true
