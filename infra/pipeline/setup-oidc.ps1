param(
    [string] $ResourceGroup = 'rg-payment-webhooks',
    [string] $Repository = 'milsucruz/payment-webhook-processor',
    [string] $AppName = 'github-payment-webhook-processor'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$allowedRoles = @(
    'Azure Service Bus Data Sender',
    'Azure Service Bus Data Receiver',
    'Key Vault Secrets User'
) | ForEach-Object { az role definition list --name $_ --query '[0].name' -o tsv }

function Write-JsonFile([object] $value) {
    $path = New-TemporaryFile
    $value | ConvertTo-Json -Depth 5 | Set-Content -Path $path -Encoding utf8
    return $path
}

$appId = az ad app list --display-name $AppName --query '[0].appId' -o tsv
if (-not $appId) {
    $appId = az ad app create --display-name $AppName --sign-in-audience AzureADMyOrg --query appId -o tsv
}

$principalId = az ad sp list --filter "appId eq '$appId'" --query '[0].id' -o tsv
if (-not $principalId) {
    $principalId = az ad sp create --id $appId --query id -o tsv
}

$subjectPrefix = gh api "repos/$Repository/actions/oidc/customization/sub" --jq '.sub_claim_prefix'
$credential = Write-JsonFile @{
    name      = 'main'
    issuer    = 'https://token.actions.githubusercontent.com'
    subject   = "${subjectPrefix}:ref:refs/heads/main"
    audiences = @('api://AzureADTokenExchange')
}
$credentialNames = az ad app federated-credential list --id $appId --query '[].name' -o tsv
if ($credentialNames -contains 'main') {
    az ad app federated-credential update --id $appId --federated-credential-id main --parameters $credential | Out-Null
} else {
    az ad app federated-credential create --id $appId --parameters $credential | Out-Null
}

$scope = az group show -n $ResourceGroup --query id -o tsv
$assigned = az role assignment list --scope $scope --assignee $principalId --query '[].roleDefinitionName' -o tsv

if ($assigned -notcontains 'Contributor') {
    az role assignment create --scope $scope --assignee-object-id $principalId --assignee-principal-type ServicePrincipal `
        --role Contributor | Out-Null
}

if ($assigned -notcontains 'Role Based Access Control Administrator') {
    $roleList = $allowedRoles -join ', '
    $condition = "((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR " +
        "(@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$roleList})) AND " +
        "((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})) OR " +
        "(@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$roleList}))"
    $roleDefinitionId = az role definition list --name 'Role Based Access Control Administrator' --query '[0].id' -o tsv
    $body = Write-JsonFile @{
        properties = @{
            roleDefinitionId = $roleDefinitionId
            principalId      = $principalId
            principalType    = 'ServicePrincipal'
            condition        = $condition
            conditionVersion = '2.0'
        }
    }
    $name = [guid]::NewGuid()
    az rest --method put --url "https://management.azure.com$scope/providers/Microsoft.Authorization/roleAssignments/${name}?api-version=2022-04-01" `
        --body "@$body" | Out-Null
}

$account = az account show --query '{tenant:tenantId,subscription:id}' -o json | ConvertFrom-Json

gh variable set AZURE_CLIENT_ID --repo $Repository --body $appId
gh variable set AZURE_TENANT_ID --repo $Repository --body $account.tenant
gh variable set AZURE_SUBSCRIPTION_ID --repo $Repository --body $account.subscription

Write-Output "Pipeline identity: clientId $appId, principalId $principalId"
