<#
One-time setup, run by a person (not CI) after `az login`. Safe to re-run.

Creates what the infra workflow needs before it can run:
- rg-expenses-bootstrap: holds the identity GitHub Actions signs in as (OIDC, no stored passwords).
  Never deleted, so the app resource group can be torn down and rebuilt without losing deploy access.
- rg-expenses-prod: the app resource group. The deploy identity gets Contributor here, plus the right
  to assign only the roles the templates use.
- A subscription-level custom role that only allows purging soft-deleted Key Vaults and Cognitive
  Services accounts, which the workflow's `recreate` mode needs to reuse the same resource names.
- The Entra group that administers Azure SQL (you and the deploy identity).
#>
param(
    [Parameter(Mandatory)] [string] $SubscriptionId,
    [string] $Location = 'westus2',
    [string] $AppResourceGroup = 'rg-expenses-prod',
    [string] $BootstrapResourceGroup = 'rg-expenses-bootstrap',
    [string] $GitHubRepo = 'kesavadeekshitjedi/ExpensesAppGenAI',
    [string] $GitHubEnvironment = 'production',
    [string] $DeployIdentityName = 'id-expenses-github',
    [string] $SqlAdminGroupName = 'Expenses SQL Admins',
    [string] $PurgeRoleName = 'Expenses Soft-Delete Purger'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

# Roles the deploy identity may hand out when Bicep assigns roles to the API identity.
# Keep in sync with the roleAssignments in infra/modules.
$assignableRoleIds = @(
    '7f951dda-4ed3-4680-a7ca-43fe172d538d' # AcrPull
    'ba92f5b4-2d11-453d-a403-e96b0029c9fe' # Storage Blob Data Contributor
    '4633458b-17de-408a-b874-0445c86b69e6' # Key Vault Secrets User
    'a97b65f3-24c7-4388-baec-2e87135dc908' # Cognitive Services User
)

function Invoke-WithRetry([scriptblock] $Action, [string] $What) {
    # New identities and role definitions take a little while to become visible.
    for ($attempt = 1; ; $attempt++) {
        try { return & $Action }
        catch {
            if ($attempt -ge 8) { throw }
            Write-Host "  $What not ready yet, retrying in 15s ($attempt/8)..."
            Start-Sleep -Seconds 15
        }
    }
}

# az.cmd is a batch file, so characters like ( ) ! & | in arguments can be mangled by cmd.exe.
# Complex values are passed through files using the CLI's @file syntax instead.
function New-ArgFile([string] $Content) {
    $path = New-TemporaryFile
    Set-Content -Path $path -Value $Content -NoNewline
    return "@$path"
}

function Get-RoleAssignments([string] $Scope, [string] $PrincipalId) {
    $all = az role assignment list --scope $Scope | ConvertFrom-Json
    return @($all | Where-Object { $_.principalId -eq $PrincipalId -and $_.scope -eq $Scope })
}

function Set-RoleAssignment([string] $Scope, [string] $PrincipalId, [string] $Role, [string] $Condition) {
    $existing = @(Get-RoleAssignments -Scope $Scope -PrincipalId $PrincipalId | Where-Object { $_.roleDefinitionName -eq $Role })
    if ($existing.Count -eq 1 -and [string]$existing[0].condition -eq $Condition) { return }
    foreach ($assignment in $existing) {
        az role assignment delete --ids $assignment.id --output none
    }
    $arguments = @(
        'role', 'assignment', 'create',
        '--assignee-object-id', $PrincipalId, '--assignee-principal-type', 'ServicePrincipal',
        '--role', $Role, '--scope', $Scope, '--output', 'none'
    )
    if ($Condition) {
        $arguments += @('--condition', (New-ArgFile $Condition), '--condition-version', '2.0')
    }
    Invoke-WithRetry -What "Role assignment '$Role'" -Action { az @arguments }
}

Write-Host "Selecting subscription $SubscriptionId"
az account set --subscription $SubscriptionId
$tenantId = az account show --query tenantId -o tsv
$subscriptionScope = "/subscriptions/$SubscriptionId"

Write-Host 'Registering resource providers (first time can take a few minutes)'
$providers = @(
    'Microsoft.App', 'Microsoft.ContainerRegistry', 'Microsoft.Sql', 'Microsoft.Storage',
    'Microsoft.KeyVault', 'Microsoft.CognitiveServices', 'Microsoft.Communication', 'Microsoft.Web',
    'Microsoft.OperationalInsights', 'Microsoft.Insights', 'Microsoft.ManagedIdentity'
)
foreach ($provider in $providers) {
    az provider register --namespace $provider --wait --output none
}

Write-Host "Creating resource groups $BootstrapResourceGroup and $AppResourceGroup in $Location"
az group create --name $BootstrapResourceGroup --location $Location --tags app=expenses purpose=bootstrap --output none
az group create --name $AppResourceGroup --location $Location --tags app=expenses environment=prod --output none
$appResourceGroupId = az group show --name $AppResourceGroup --query id -o tsv

Write-Host "Creating deploy identity $DeployIdentityName"
$identity = az identity create --name $DeployIdentityName --resource-group $BootstrapResourceGroup --location $Location | ConvertFrom-Json

$credentialName = "github-$GitHubEnvironment"
$credentials = az identity federated-credential list --identity-name $DeployIdentityName --resource-group $BootstrapResourceGroup | ConvertFrom-Json
if (-not ($credentials | Where-Object { $_.name -eq $credentialName })) {
    Write-Host "Trusting GitHub Actions runs in environment '$GitHubEnvironment' of $GitHubRepo"
    az identity federated-credential create `
        --name $credentialName `
        --identity-name $DeployIdentityName `
        --resource-group $BootstrapResourceGroup `
        --issuer 'https://token.actions.githubusercontent.com' `
        --subject "repo:${GitHubRepo}:environment:${GitHubEnvironment}" `
        --audiences 'api://AzureADTokenExchange' `
        --output none
}

Write-Host "Granting Contributor on $AppResourceGroup"
Set-RoleAssignment -Scope $appResourceGroupId -PrincipalId $identity.principalId -Role 'Contributor'

Write-Host 'Granting role-assignment rights, limited to the roles the templates assign'
$roleList = $assignableRoleIds -join ', '
$condition = "((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$roleList})) AND ((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$roleList}))"
Set-RoleAssignment -Scope $appResourceGroupId -PrincipalId $identity.principalId -Role 'Role Based Access Control Administrator' -Condition $condition

Write-Host "Creating custom role '$PurgeRoleName'"
$purgeDescription = 'Purge soft-deleted Key Vaults and Cognitive Services accounts so the Home Expenses app can be rebuilt with the same names.'
$purgeActions = @(
    'Microsoft.KeyVault/deletedVaults/read'
    'Microsoft.KeyVault/locations/deletedVaults/read'
    'Microsoft.KeyVault/locations/deletedVaults/purge/action'
    'Microsoft.KeyVault/locations/operationResults/read'
    'Microsoft.CognitiveServices/deletedAccounts/read'
    'Microsoft.CognitiveServices/locations/resourceGroups/deletedAccounts/read'
    'Microsoft.CognitiveServices/locations/resourceGroups/deletedAccounts/delete'
    'Microsoft.CognitiveServices/locations/operationResults/read'
)
$existingRole = @(az role definition list --custom-role-only true --name $PurgeRoleName | ConvertFrom-Json)
if ($existingRole.Count -gt 0) {
    # `update` takes the same shape `list` returns (roleName, permissions[]), unlike `create`.
    $definition = $existingRole[0]
    $definition.description = $purgeDescription
    $definition.permissions[0].actions = $purgeActions
    $definition.assignableScopes = @($subscriptionScope)
    az role definition update --role-definition (New-ArgFile ($definition | ConvertTo-Json -Depth 10)) --output none
} else {
    $purgeRole = [ordered]@{
        Name             = $PurgeRoleName
        Description      = $purgeDescription
        IsCustom         = $true
        Actions          = $purgeActions
        NotActions       = @()
        AssignableScopes = @($subscriptionScope)
    } | ConvertTo-Json
    az role definition create --role-definition (New-ArgFile $purgeRole) --output none
}
Set-RoleAssignment -Scope $subscriptionScope -PrincipalId $identity.principalId -Role $PurgeRoleName

Write-Host "Creating Entra group '$SqlAdminGroupName'"
$group = az ad group list --display-name $SqlAdminGroupName --query '[0]' | ConvertFrom-Json
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
