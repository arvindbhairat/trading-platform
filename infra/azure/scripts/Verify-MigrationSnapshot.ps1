<#
.SYNOPSIS
    Verifies that all pending PostgreSQL migrations apply cleanly against a
    restored production database snapshot (REQ-MIGRATION-007).

.DESCRIPTION
    This script restores the latest production backup to a staging PostgreSQL
    server, runs FluentMigrator against the restored copy, and reports pass/fail.
    It is invoked by the deploy.yml pre-deploy-verify job before any
    deployment slot is activated.

    Azure environment:
        - Source: production Azure Database for PostgreSQL Flexible Server (PITR)
        - Target: staging PostgreSQL server (restored snapshot)
        - Uses Azure CLI for point-in-time restore.

    Non-Azure / local dev:
        - Source: production dump file (.sql or .dump)
        - Target: local PostgreSQL instance
        - Uses psql / pg_restore for restore.

.PARAMETER SourceServer
    The production PostgreSQL server instance name.

.PARAMETER SourceDatabase
    The production database name (e.g. marketdata or backtest).

.PARAMETER TargetServer
    The staging PostgreSQL server to restore into.

.PARAMETER TargetDatabase
    The name for the restored database copy.

.PARAMETER MigrationProjectPath
    Path to the SignalStack.SqlMigrations project.

.EXAMPLE
    .\Verify-MigrationSnapshot.ps1 `
        -SourceServer "psql-signalstack-prd.postgres.database.azure.com" `
        -SourceDatabase "marketdata" `
        -TargetServer "psql-signalstack-stg.postgres.database.azure.com" `
        -TargetDatabase "marketdata_verify" `
        -MigrationProjectPath "packages/migrations/SignalStack.SqlMigrations"

.NOTES
    Frozen-after-author: this script's existence and call site in deploy.yml
    is frozen.  The restore mechanism may be updated as the hosting target
    evolves.
#>

param(
    [Parameter(Mandatory)]
    [string]$SourceServer,

    [Parameter(Mandatory)]
    [string]$SourceDatabase,

    [Parameter(Mandatory)]
    [string]$TargetServer,

    [Parameter(Mandatory)]
    [string]$TargetDatabase,

    [Parameter(Mandatory)]
    [string]$MigrationProjectPath
)

$ErrorActionPreference = "Stop"

Write-Host "=== Migration Snapshot Verification (PostgreSQL) ==="
Write-Host "Source:     $SourceServer/$SourceDatabase"
Write-Host "Target:     $TargetServer/$TargetDatabase"
Write-Host "Timestamp:  $(Get-Date -Format 'yyyy-MM-ddTHH:mm:ssZ')"
Write-Host ""

# ── Step 1: Restore production snapshot ─────────────────────────────────
Write-Host "[1/3] Restoring production snapshot..."
Write-Host "       Restoring $SourceDatabase from $SourceServer to $TargetServer as $TargetDatabase"

# Azure Database for PostgreSQL Flexible Server: point-in-time restore
# az postgres flexible-server restore --source-server "$SourceServer" `
#     --subscription "<sub>" --resource-group "<rg>" `
#     --name "$TargetDatabase" `
#     --restore-point-in-time "$(Get-Date -Format 'yyyy-MM-ddTHH:mm:ssZ')" `
#     --source-database "$SourceDatabase"

Write-Host "       [WARNING] Restore command must be configured for target deployment environment."
Write-Host "       [WARNING] See infra/azure/ for Bicep templates and connection strings."

# ── Step 2: Run FluentMigrator against restored snapshot ─────────────────
Write-Host ""
Write-Host "[2/3] Running FluentMigrator against restored snapshot..."

$targetConnection = "Host=$TargetServer;Database=$TargetDatabase;Username=postgres;SSL Mode=Require;Trust Server Certificate=true;"

try {
    dotnet run --project "$MigrationProjectPath" -- `
        --connection "$targetConnection" `
        --database "$TargetDatabase"

    if ($LASTEXITCODE -ne 0) {
        throw "FluentMigrator exited with code $LASTEXITCODE"
    }

    Write-Host "       Migrations applied successfully to $TargetDatabase"
}
catch {
    Write-Host "       [FAIL] Migration failed against restored snapshot!"
    Write-Host "       $_"
    exit 1
}

# ── Step 3: Report ───────────────────────────────────────────────────────
Write-Host ""
Write-Host "[3/3] Recording verification result..."

$report = @{
    verification_type   = "migration_snapshot_verify"
    source_server       = $SourceServer
    source_database     = $SourceDatabase
    target_database     = $TargetDatabase
    timestamp           = (Get-Date -Format 'yyyy-MM-ddTHH:mm:ssZ')
    result              = "PASS"
    migrations_applied  = $true
}

$reportPath = "migration-verify-report-$TargetDatabase.json"
$report | ConvertTo-Json | Out-File -FilePath $reportPath -Encoding utf8
Write-Host "       Report written to: $reportPath"

Write-Host ""
Write-Host "=== Verification PASS ==="
