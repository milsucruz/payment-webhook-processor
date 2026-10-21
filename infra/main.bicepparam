using 'main.bicep'

param namePrefix = 'payment-webhooks'
param sqlAdminLogin = readEnvironmentVariable('SQL_ADMIN_LOGIN')
param sqlAdminObjectId = readEnvironmentVariable('SQL_ADMIN_OBJECT_ID')
param imageRegistry = 'ghcr.io/milsucruz/payment-webhook-processor'
param imageTag = readEnvironmentVariable('IMAGE_TAG', 'manual')
param faultInjectionEnabled = true
