param(
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [Parameter(Mandatory)] [string] $ServerName,
    [string] $DatabaseName = 'Payments',
    [string] $SqlCmd = 'sqlcmd'
)

$ErrorActionPreference = 'Stop'

function Get-IdentitySid([string] $role) {
    $clientId = az identity show -g $ResourceGroup -n "id-payment-webhooks-$role" --query clientId -o tsv
    $hex = ([guid] $clientId).ToByteArray() | ForEach-Object { $_.ToString('X2') }
    return '0x' + ($hex -join '')
}

$apiSid = Get-IdentitySid 'api'
$workerSid = Get-IdentitySid 'worker'
$migratorSid = Get-IdentitySid 'migrator'

& $SqlCmd -S "$ServerName.database.windows.net" -d $DatabaseName --authentication-method ActiveDirectoryAzCli -b `
    -i (Join-Path $PSScriptRoot 'create-users.sql') `
    -v ApiSid=$apiSid WorkerSid=$workerSid MigratorSid=$migratorSid
if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed with exit code $LASTEXITCODE" }
