// Backup geo-redundancy scaffold for MongoDB Atlas PITR and Azure recovery configuration.
// REQ-BCP-001: MongoDB operational data RPO 1h / RTO 2h; Atlas PITR with >=24h history.
// REQ-BCP-005: All backups geo-redundantly replicated to South India.
// REQ-LEGAL-009: Data residency — both primary (Central India) and secondary (South India) within India.
//
// MongoDB Atlas PITR NOTE: Atlas PITR is configured through the Atlas control plane
// (Atlas UI or Atlas Administration API), not via Azure Bicep. The required Atlas settings are:
//   - Cluster tier M10 or higher (continuous PITR requires a replica set)
//   - Continuous Cloud Backup: enabled
//   - Point-in-Time Restore: enabled
//   - Restore window: >= 24 hours (set to 48h for operational margin)
//   - Backup region: Atlas India region (Mumbai or the closest equivalent)
//   - Cloud Provider Snapshots: enabled with geo-redundant snapshot export to a secondary bucket
// These settings must be validated post-deployment via the Atlas control plane.
// See docs/operations/runbooks/mongodb-pitr-restore.md for restore procedure.

@description('Resource group location for the geo-redundancy resources. Must be an Azure India region.')
@allowed([
  'centralindia'
  'southindia'
])
param location string = 'centralindia'

@description('Secondary region for geo-redundant restores. Must remain within India per REQ-LEGAL-009.')
@allowed([
  'southindia'
  'centralindia'
])
param secondaryLocation string = 'southindia'

@description('Name prefix for Recovery Services Vault resources.')
param vaultNamePrefix string = 'signalstack'

// ─────────────────────────────────────────────────────────────
// Recovery Services Vault — primary region
// Provides Azure-native backup management for non-Atlas workloads.
// ─────────────────────────────────────────────────────────────
resource primaryRecoveryVault 'Microsoft.RecoveryServices/vaults@2024-01-01' = {
  name: '${vaultNamePrefix}-rsv-primary'
  location: location
  sku: {
    name: 'RS0'
    tier: 'Standard'
  }
  properties: {
    publicNetworkAccess: 'Disabled'
  }
}

// Cross-region replication settings: enable CRR so that backup data is
// asynchronously replicated from Central India to South India.
resource primaryVaultBackupConfig 'Microsoft.RecoveryServices/vaults/backupconfig@2024-01-01' = {
  parent: primaryRecoveryVault
  name: 'vaultconfig'
  properties: {
    crossRegionRestoreFlag: true    // Enables restore from paired South India region
    storageModelType: 'GeoRedundant' // RA-GRS storage — satisfies REQ-BCP-005
    softDeleteFeatureState: 'AlwaysON' // Align with Key Vault purge-protection philosophy
  }
}

// ─────────────────────────────────────────────────────────────
// Recovery Services Vault — South India secondary
// Passive restore target per REQ-BCP-005; no active workloads.
// ─────────────────────────────────────────────────────────────
resource secondaryRecoveryVault 'Microsoft.RecoveryServices/vaults@2024-01-01' = {
  name: '${vaultNamePrefix}-rsv-secondary'
  location: secondaryLocation
  sku: {
    name: 'RS0'
    tier: 'Standard'
  }
  properties: {
    publicNetworkAccess: 'Disabled'
  }
}

resource secondaryVaultBackupConfig 'Microsoft.RecoveryServices/vaults/backupconfig@2024-01-01' = {
  parent: secondaryRecoveryVault
  name: 'vaultconfig'
  properties: {
    crossRegionRestoreFlag: false   // Secondary vault is restore target; no outbound CRR
    storageModelType: 'LocallyRedundant' // Data already replicated here from primary
    softDeleteFeatureState: 'AlwaysON'
  }
}

// ─────────────────────────────────────────────────────────────
// Market data backup policy — matches REQ-BCP-002 RPO 24h / RTO 4h
// Daily full backup retained for 5 days; weekly for 4 weeks; monthly for 3 months.
// ─────────────────────────────────────────────────────────────
resource marketDataBackupPolicy 'Microsoft.RecoveryServices/vaults/backupPolicies@2024-01-01' = {
  parent: primaryRecoveryVault
  name: 'market-data-backup-policy'
  properties: {
    backupManagementType: 'AzureWorkload'
    workLoadType: 'SQLDataBase'
    settings: {
      isCompression: true
      issqlcompression: true
      timeZone: 'India Standard Time'
    }
    subProtectionPolicy: [
      {
        policyType: 'Full'
        schedulePolicy: {
          schedulePolicyType: 'SimpleSchedulePolicy'
          scheduleRunFrequency: 'Daily'
          scheduleRunTimes: [
            '2000-01-01T01:00:00Z'  // 01:00 UTC = 06:30 IST — pre-market, after DataSync window
          ]
        }
        retentionPolicy: {
          retentionPolicyType: 'LongTermRetentionPolicy'
          dailySchedule: {
            retentionTimes: [
              '2000-01-01T01:00:00Z'
            ]
            retentionDuration: {
              count: 5
              durationType: 'Days'
            }
          }
          weeklySchedule: {
            daysOfTheWeek: [
              'Sunday'
            ]
            retentionTimes: [
              '2000-01-01T01:00:00Z'
            ]
            retentionDuration: {
              count: 4
              durationType: 'Weeks'
            }
          }
          monthlySchedule: {
            retentionScheduleFormatType: 'Weekly'
            retentionScheduleWeekly: {
              daysOfTheWeek: [
                'Sunday'
              ]
              weeksOfTheMonth: [
                'First'
              ]
            }
            retentionTimes: [
              '2000-01-01T01:00:00Z'
            ]
            retentionDuration: {
              count: 3
              durationType: 'Months'
            }
          }
        }
      }
      {
        // Transaction log backups every 6 hours per REQ-BCP-002.
        policyType: 'Log'
        schedulePolicy: {
          schedulePolicyType: 'LogSchedulePolicy'
          scheduleFrequencyInMins: 360   // 6-hour interval
        }
        retentionPolicy: {
          retentionPolicyType: 'SimpleRetentionPolicy'
          retentionDuration: {
            count: 5
            durationType: 'Days'
          }
        }
      }
    ]
  }
}

// ─────────────────────────────────────────────────────────────
// Backtest backup policy — matches REQ-BCP-003 RPO 7d / RTO 8h
// Weekly full backup retained for 8 weeks.
// ─────────────────────────────────────────────────────────────
resource backtestBackupPolicy 'Microsoft.RecoveryServices/vaults/backupPolicies@2024-01-01' = {
  parent: primaryRecoveryVault
  name: 'backtest-backup-policy'
  properties: {
    backupManagementType: 'AzureWorkload'
    workLoadType: 'SQLDataBase'
    settings: {
      isCompression: true
      issqlcompression: true
      timeZone: 'India Standard Time'
    }
    subProtectionPolicy: [
      {
        policyType: 'Full'
        schedulePolicy: {
          schedulePolicyType: 'SimpleSchedulePolicy'
          scheduleRunFrequency: 'Weekly'
          scheduleRunDays: [
            'Sunday'
          ]
          scheduleRunTimes: [
            '2000-01-01T02:00:00Z'  // 02:00 UTC = 07:30 IST
          ]
        }
        retentionPolicy: {
          retentionPolicyType: 'LongTermRetentionPolicy'
          weeklySchedule: {
            daysOfTheWeek: [
              'Sunday'
            ]
            retentionTimes: [
              '2000-01-01T02:00:00Z'
            ]
            retentionDuration: {
              count: 8
              durationType: 'Weeks'
            }
          }
        }
      }
      {
        // Log backups every 24h; acceptable for REQ-BCP-003 (longer RPO tolerance).
        policyType: 'Log'
        schedulePolicy: {
          schedulePolicyType: 'LogSchedulePolicy'
          scheduleFrequencyInMins: 1440  // 24-hour interval
        }
        retentionPolicy: {
          retentionPolicyType: 'SimpleRetentionPolicy'
          retentionDuration: {
            count: 8
            durationType: 'Days'
          }
        }
      }
    ]
  }
}

output primaryRecoveryVaultId string = primaryRecoveryVault.id
output secondaryRecoveryVaultId string = secondaryRecoveryVault.id
output marketDataBackupPolicyId string = marketDataBackupPolicy.id
output backtestBackupPolicyId string = backtestBackupPolicy.id
