param(
  [Parameter(Mandatory = $true)]
  [string]$ResourceGroup,

  [Parameter(Mandatory = $true)]
  [string]$PlanName
)

$ErrorActionPreference = 'Stop'

$plan = az appservice plan show `
  --resource-group $ResourceGroup `
  --name $PlanName `
  --output json | ConvertFrom-Json

if (-not $plan) {
  throw "Worker App Service plan '$PlanName' was not found in resource group '$ResourceGroup'."
}

$workerCount = if ($plan.sku -and $plan.sku.capacity -ne $null) { [int]$plan.sku.capacity } else { -1 }
if ($workerCount -ne 1) {
  throw "Worker singleton preflight failed: expected App Service plan capacity 1, found $workerCount."
}

$autoscale = az monitor autoscale list `
  --resource-group $ResourceGroup `
  --output json | ConvertFrom-Json

$attachedAutoscaleRules = @($autoscale | Where-Object {
  $_.enabled -eq $true -and $_.targetResourceUri -like "*serverfarms/$PlanName"
})

if ($attachedAutoscaleRules.Count -gt 0) {
  throw "Worker singleton preflight failed: autoscale is attached to App Service plan '$PlanName'."
}

Write-Host "Worker singleton preflight passed for App Service plan '$PlanName'."
