<#
One-time setup, run by a person (not CI) after `az login`.
Creates what the infra workflow needs before it can run: the resource group, the identity GitHub Actions
signs in as (OIDC, no stored passwords), and the Entra group that administers Azure SQL.
Safe to re-run.
#>
param(
    [Parameter(Mandatory)] [string] $SubscriptionId,
    [string] $Location = 'westus2',
    [string] $ResourceGroup = 'rg-expenses-prod',
    [string] $GitHubRepo = 'kesavadeekshitjedi/ExpensesAppGenAI',
    [string] $GitHubEnvironment = 'production',
    [string] $DeployIdentityName = 'id-expenses-github',
    [string] $SqlAdminGroupName = 'Expenses SQL Admins'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

# Roles the deploy identity may hand out when Bicep assigns roles to the API identity.
# Keep in sync with the roleAssignments in infra/modules.
$assignableRoleIds = @(
    '7f951dff-4ed5-43fd-a50b-7a2d9b8b3a3a' # AcrPull
    'ba92f5b4-2d11-453d-a403-e96b0029c9fe' # Storage Blob Data Contributor
    '4633458b-17de-408a-b874-0445c86b69e6' # Key Vault Secrets User
    'a97b65f3-24c7-4388-baec-2e87135dc908' # Cognitive Services User
)

function Invoke-WithRetry([scriptblock] $Action, [string] $What) {
    # Newly created identities take a little while to appear in Entra ID.
    for ($attempt = 1; ; $attempt++) {
        try { return & $Action }
        catch {
            if ($attempt -ge 8) { throw }
            Write-Host "  $What not ready yet, retrying in 15s ($attempt/8)..."
            Start-Sleep -Seconds 15
        }
    }
}

Write-Host "Selecting subscription $SubscriptionId"
az account set --subscription $SubscriptionId
$tenantId = az account show --query tenantId -o tsv

Write-Host 'Registering resource providers (first time can take a few minutes)'
$providers = @(
    'Microsoft.App', 'Microsoft.ContainerRegistry', 'Microsoft.Sql', 'Microsoft.Storage',
    'Microsoft.KeyVault', 'Microsoft.CognitiveServices', 'Microsoft.Communication', 'Microsoft.Web',
    'Microsoft.OperationalInsights', 'Microsoft.Insights', 'Microsoft.ManagedIdentity'
)
foreach ($provider in $providers) {
    az provider register --namespace $provider --wait --output none
}

Write-Host "Creating resource group $ResourceGroup in $Location"
az group create --name $ResourceGroup --location $Location --tags app=expenses environment=prod --output none
$resourceGroupId = az group show --name $ResourceGroup --query id -o tsv

Write-Host "Creating deploy identity $DeployIdentityName"
$identity = az identity create --name $DeployIdentityName --resource-group $ResourceGroup --location $Location | ConvertFrom-Json

$credentialName = "github-$GitHubEnvironment"
$existingCredential = az identity federated-credential list --identity-name $DeployIdentityName --resource-group $ResourceGroup --query "[?name=='$credentialName'].name" -o tsv
if (-not $existingCredential) {
    Write-Host "Trusting GitHub Actions runs in environment '$GitHubEnvironment' of $GitHubRepo"
    az identity federated-credential create `
        --name $credentialName `
        --identity-name $DeployIdentityName `
        --resource-group $ResourceGroup `
        --issuer 'https://token.actions.githubusercontent.com' `
        --subject "repo:${GitHubRepo}:environment:${GitHubEnvironment}" `
        --audiences 'api://AzureADTokenExchange' `
        --output none
}

Write-Host 'Granting Contributor on the resource group'
Invoke-WithRetry -What 'Deploy identity' -Action {
    az role assignment create --assignee-object-id $identity.principalId --assignee-principal-type ServicePrincipal `
        --role 'Contributor' --scope $resourceGroupId --output none
}

Write-Host 'Granting role-assignment rights, limited to the roles the templates assign'
$roleList = $assignableRoleIds -join ', '
$condition = "((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$roleList})) AND ((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$roleList}))"
$rbacAdminRole = 'Role Based Access Control Administrator'
$existingRbacAdmin = az role assignment list --assignee $identity.principalId --role $rbacAdminRole --scope $resourceGroupId --query '[0].id' -o tsv
if ($existingRbacAdmin) {
    az role assignment delete --ids $existingRbacAdmin --output none
}
az role assignment create --assignee-object-id $identity.principalId --assignee-principal-type ServicePrincipal `
    --role $rbacAdminRole --scope $resourceGroupId `
    --condition $condition --condition-version '2.0' --output none

Write-Host "Creating Entra group '$SqlAdminGroupName'"
$group = az ad group list --filter "displayName eq '$SqlAdminGroupName'" --query '[0]' | ConvertFrom-Json
if (-not $group) {
    $group = az ad group create --display-name $SqlAdminGroupName --mail-nickname 'expenses-sql-admins' | ConvertFrom-Json
}

$signedInUserId = az ad signed-in-user show --query id -o tsv
foreach ($memberId in @($signedInUserId, $identity.principalId)) {
    Invoke-WithRetry -What 'Group member' -Action {
        $isMember = az ad group member check --group $group.id --member-id $memberId --query value -o tsv
        if ($isMember -ne 'true') {
            az ad group member add --group $group.id --member-id $memberId --output none
        }
    }
}

Write-Host ''
Write-Host "Done. In GitHub: Settings > Environments > New environment '$GitHubEnvironment', then add these Environment variables:"
Write-Host ''
[ordered]@{
    AZURE_CLIENT_ID       = $identity.clientId
    AZURE_TENANT_ID       = $tenantId
    AZURE_SUBSCRIPTION_ID = $SubscriptionId
    SQL_ADMIN_GROUP_ID    = $group.id
} | Format-Table -HideTableHeaders -AutoSize
