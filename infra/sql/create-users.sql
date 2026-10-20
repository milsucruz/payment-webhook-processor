IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'id-payment-webhooks-api')
    CREATE USER [id-payment-webhooks-api] WITH SID = $(ApiSid), TYPE = E;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'id-payment-webhooks-worker')
    CREATE USER [id-payment-webhooks-worker] WITH SID = $(WorkerSid), TYPE = E;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'id-payment-webhooks-migrator')
    CREATE USER [id-payment-webhooks-migrator] WITH SID = $(MigratorSid), TYPE = E;
GO

ALTER ROLE db_datareader ADD MEMBER [id-payment-webhooks-api];
ALTER ROLE db_datareader ADD MEMBER [id-payment-webhooks-worker];
ALTER ROLE db_datawriter ADD MEMBER [id-payment-webhooks-worker];
ALTER ROLE db_datareader ADD MEMBER [id-payment-webhooks-migrator];
ALTER ROLE db_datawriter ADD MEMBER [id-payment-webhooks-migrator];
ALTER ROLE db_ddladmin ADD MEMBER [id-payment-webhooks-migrator];
GO
