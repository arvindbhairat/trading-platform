// SQL Server backup configuration for market-data and backtest databases.
// REQ-BCP-002: Market data — RPO 24h, RTO 4h. Daily full + transaction log every 6h on trading days.
// REQ-BCP-003: Backtest — RPO 7 days, RTO 8h. Weekly full backup sufficient.
// REQ-BCP-005: Geo-redundant backup storage; secondary in South India per REQ-LEGAL-009.
//
// Azure SQL Database performs transaction log backups automatically (every 5–12 minutes),
// which satisfies the "at least every 6 hours" clause in REQ-BCP-002. Daily full backups
// and differential backups are also automatic. The backup retention window below governs
// how far back in time a point-in-time restore can reach.

@description('SQL Server logical server name.')
param sqlServerName string

@description('Primary deployment region. Must be an Azure India region per REQ-LEGAL-009.')
@allowed([
  'centralindia'
  'southindia'
])
param location string = 'centralindia'

@description('Secondary region for geo-redundant restores per REQ-BCP-005.')
@allowed([
  'southindia'
  'centralindia'
])
param secondaryLocation string = 'southindia'

@description('Azure AD admin object ID for the SQL Server.')
param sqlAdminObjectId string

@description('Azure AD admin login name.')
param sqlAdminLogin string

resource sqlServer 'Microsoft.Sql/servers@2023-02-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    // Managed Identity authentication preferred; no SQL auth password required.
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'User'
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true
    }
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'  // Enforce VNet-only access
  }
}

// ─────────────────────────────────────────────────────────────
// Market data database
// REQ-BCP-002: RPO 24h — PITR retention 2 days covers the 24h window with margin.
// Geo-redundant backup storage (RA-GRS) satisfies REQ-BCP-005.
// ─────────────────────────────────────────────────────────────
resource marketDataDb 'Microsoft.Sql/servers/databases@2023-02-01-preview' = {
  parent: sqlServer
  name: 'marketdata'
  location: location
  sku: {
    name: 'GP_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    requestedBackupStorageRedundancy: 'GeoZone' // Geo-redundant zone-redundant backup storage
    zoneRedundant: false                         // Phase A: single-AZ is sufficient
    readScale: 'Disabled'
  }
}

// Short-term retention: 2 days provides the 24h PITR window required by REQ-BCP-002,
// with 1 day of margin for the restore window.
resource marketDataShortTermRetention 'Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies@2023-02-01-preview' = {
  parent: marketDataDb
  name: 'default'
  properties: {
    retentionDays: 2
    diffBackupIntervalInHours: 12               // Differential backups every 12h to minimise restore time
  }
}

// Long-term retention: weekly backup kept for 4 weeks; monthly for 3 months.
// Provides the full compliance trail beyond the PITR window for audit purposes.
resource marketDataLongTermRetention 'Microsoft.Sql/servers/databases/backupLongTermRetentionPolicies@2023-02-01-preview' = {
  parent: marketDataDb
  name: 'default'
  properties: {
    weeklyRetention: 'P4W'   // 4 weekly backups retained
    monthlyRetention: 'P3M'  // 3 monthly backups retained
    yearlyRetention: 'P0D'   // No forced yearly retention beyond LTR above
    weekOfYear: 1
  }
}

// ─────────────────────────────────────────────────────────────
// Backtest database
// REQ-BCP-003: RPO 7 days — PITR retention 7 days. Weekly full is implicit via Azure SQL.
// Geo-redundant backup storage satisfies REQ-BCP-005.
// Backtest results are reproducible from market data + Signal code + parameters,
// so a longer RTO (8h) is acceptable and a smaller SKU is used.
// ─────────────────────────────────────────────────────────────
resource backtestDb 'Microsoft.Sql/servers/databases@2023-02-01-preview' = {
  parent: sqlServer
  name: 'backtest'
  location: location
  sku: {
    name: 'GP_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    requestedBackupStorageRedundancy: 'GeoZone'
    zoneRedundant: false
    readScale: 'Disabled'
  }
}

// Short-term retention: 7 days to meet the REQ-BCP-003 RPO.
resource backtestShortTermRetention 'Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies@2023-02-01-preview' = {
  parent: backtestDb
  name: 'default'
  properties: {
    retentionDays: 7
    diffBackupIntervalInHours: 24               // Daily differentials; weekly full is the base
  }
}

// Long-term retention: weekly backup kept for 8 weeks.
resource backtestLongTermRetention 'Microsoft.Sql/servers/databases/backupLongTermRetentionPolicies@2023-02-01-preview' = {
  parent: backtestDb
  name: 'default'
  properties: {
    weeklyRetention: 'P8W'
    monthlyRetention: 'P0D'
    yearlyRetention: 'P0D'
    weekOfYear: 1
  }
}

// ─────────────────────────────────────────────────────────────
// Geo-replication: passive failover group targeting South India
// REQ-BCP-005: secondary is restore-target only; no active-active footprint in Phase A.
// ─────────────────────────────────────────────────────────────
resource sqlServerSecondary 'Microsoft.Sql/servers@2023-02-01-preview' = {
  name: '${sqlServerName}-secondary'
  location: secondaryLocation                   // South India per REQ-BCP-005 / REQ-LEGAL-009
  properties: {
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'User'
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true
    }
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
  }
}

// Failover group — read-write listener points to primary; read-only listener points to secondary.
// Automatic failover is NOT configured (Phase A: manual failover only per REQ-BCP-005).
resource failoverGroup 'Microsoft.Sql/servers/failoverGroups@2023-02-01-preview' = {
  parent: sqlServer
  name: '${sqlServerName}-fog'
  properties: {
    partnerServers: [
      {
        id: sqlServerSecondary.id
      }
    ]
    databases: [
      marketDataDb.id
      backtestDb.id
    ]
    readWriteEndpoint: {
      failoverPolicy: 'Manual'      // Operator-initiated failover only; no auto-failover in Phase A
    }
    readOnlyEndpoint: {
      failoverPolicy: 'Disabled'
    }
  }
}

output sqlServerId string = sqlServer.id
output marketDataDbId string = marketDataDb.id
output backtestDbId string = backtestDb.id
output failoverGroupId string = failoverGroup.id
output secondarySqlServerId string = sqlServerSecondary.id
