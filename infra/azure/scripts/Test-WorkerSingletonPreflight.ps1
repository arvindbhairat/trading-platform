<#
.SYNOPSIS
    Pre-flight guard: asserts the Worker App Service is running as a singleton
    with no autoscale rules attached to its hosting plan.

.DESCRIPTION
    Called by the worker-release-preflight.yml GitHub Actions workflow before
    every Worker deployment. Exits non-zero (failing the pipeline) if:
      - The App Service Plan has an instance count > 1, OR
      - An autoscale setting targets the App Service Plan.

    This enforces the non-negotiable guardrail from engineering-standards.md and
    ADR-0003: the Worker must run as exactly one instance until multi-instance
    partitioning is designed and accepted in a successor ADR.

.PARAMETER ResourceGroup
    The Azure resource group containing the App Service Plan.

.PARAMETER AppServicePlan
    The name of the App Service Plan to inspect.

.EXAMPLE
    ./Test-WorkerSingletonPreflight.ps1 -ResourceGroup rg-signalstack-prod -AppServicePlan asp-worker-prod
#>
param(
    [Parameter(Mandatory)]
    [string]$ResourceGroup,

    [Parameter(Mandatory)]
    [string]$AppServicePlan
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host "==> Checking Worker singleton invariant for plan '$AppServicePlan' in '$ResourceGroup'..."

# ── 1. Verify instance count ──────────────────────────────────────────────────
$plan = az appservice plan show `
    --resource-group $ResourceGroup `
    --name $AppServicePlan `
    --output json | ConvertFrom-Json

$currentCapacity = $plan.sku.capacity
if ($currentCapacity -ne 1) {
    Write-Error "FAIL: App Service Plan '$AppServicePlan' has $currentCapacity instance(s). Expected exactly 1."
    exit 1
}
Write-Host "  [OK] Instance count = $currentCapacity"

# ── 2. Check for autoscale settings targeting this plan ───────────────────────
$planId = $plan.id
$autoscaleRules = az monitor autoscale list `
    --resource-group $ResourceGroup `
    --output json | ConvertFrom-Json |
    Where-Object { $_.targetResourceUri -ieq $planId }

if ($autoscaleRules.Count -gt 0) {
    Write-Error "FAIL: Found $($autoscaleRules.Count) autoscale setting(s) targeting '$AppServicePlan'. Remove them before deploying."
    exit 1
}
Write-Host "  [OK] No autoscale settings found"

Write-Host "==> Pre-flight passed. Worker singleton invariant is intact."
exit 0
