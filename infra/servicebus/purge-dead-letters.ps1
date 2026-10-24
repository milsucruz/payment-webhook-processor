param(
    [string] $ResourceGroup = 'rg-payment-webhooks',
    [string] $QueueName = 'payment-events'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$namespace = az servicebus namespace list -g $ResourceGroup --query '[0].name' -o tsv
$token = az account get-access-token --resource https://servicebus.azure.net --query accessToken -o tsv
$headers = @{ Authorization = "Bearer $token" }
$url = "https://$namespace.servicebus.windows.net/$QueueName/`$DeadLetterQueue/messages/head?timeout=5"

$deleted = 0
while ($true) {
    $response = Invoke-WebRequest -Method Delete -Uri $url -Headers $headers
    if ($response.StatusCode -eq 204) {
        break
    }

    $deleted++
}

Write-Output "Deleted $deleted messages from the dead-letter of $QueueName."
