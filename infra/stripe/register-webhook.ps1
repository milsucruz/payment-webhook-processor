param(
    [string] $ResourceGroup = 'rg-payment-webhooks',
    [string] $ApiName = 'ca-payment-webhooks-api',
    [string] $ApiVersion = '2026-09-30.endive'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$events = @(
    'payment_intent.created',
    'payment_intent.processing',
    'payment_intent.requires_action',
    'payment_intent.succeeded',
    'payment_intent.payment_failed',
    'payment_intent.canceled',
    'charge.refunded'
)

$fqdn = az containerapp show -g $ResourceGroup -n $ApiName --query properties.configuration.ingress.fqdn -o tsv
$url = "https://$fqdn/webhooks/stripe"
$vaultName = az keyvault list -g $ResourceGroup --query '[0].name' -o tsv
$vaultUri = az keyvault show -n $vaultName --query properties.vaultUri -o tsv
$identity = az identity show -g $ResourceGroup -n id-payment-webhooks-api --query id -o tsv
$revision = az containerapp show -g $ResourceGroup -n $ApiName --query properties.latestRevisionName -o tsv

$existing = (stripe webhook_endpoints list -d limit=100 | ConvertFrom-Json).data | Where-Object { $_.url -eq $url }
if ($existing) {
    throw "Endpoint $($existing.id) already points to $url. Delete it (stripe webhook_endpoints delete $($existing.id)) and run again."
}

$arguments = @('-d', "url=$url", '-d', "api_version=$ApiVersion", '-d', 'description=payment-webhook-processor (Azure)')
$events | ForEach-Object { $arguments += @('-d', "enabled_events[]=$_") }
$endpoint = stripe webhook_endpoints create @arguments | ConvertFrom-Json

$secretFile = New-TemporaryFile
try {
    Set-Content -Path $secretFile -Value $endpoint.secret -NoNewline -Encoding utf8NoBOM
    az keyvault secret set --vault-name $vaultName -n stripe-webhook-secret --file $secretFile --encoding utf-8 -o none
    az containerapp secret set -g $ResourceGroup -n $ApiName -o none `
        --secrets "stripe-webhook-secret=keyvaultref:${vaultUri}secrets/stripe-webhook-secret,identityref:$identity"
    az containerapp revision restart -g $ResourceGroup -n $ApiName --revision $revision | Out-Null
} catch {
    stripe webhook_endpoints delete $endpoint.id --confirm | Out-Null
    throw
} finally {
    Remove-Item $secretFile
}

Write-Output "Endpoint $($endpoint.id) -> $url ($($endpoint.enabled_events.Count) events, API $($endpoint.api_version))"
Write-Output "Secret stored in $vaultName/stripe-webhook-secret; revision $revision restarted"
