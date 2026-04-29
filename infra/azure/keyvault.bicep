// Key Vault provisioning with BCP-hardening settings.
// REQ-BCP-004: soft-delete enabled, 90-day retention, purge protection enabled.
// REQ-BCP-008: key rotation policies (90-day cycle for encryption keys).
// REQ-LEGAL-009: deployed to Central India; geo-redundant content replicated to South India.

@description('Key Vault name.')
param keyVaultName string

@description('Primary deployment region. Must be an Azure India region per REQ-LEGAL-009.')
@allowed([
  'centralindia'
  'southindia'
])
param location string = 'centralindia'

@description('Entra ID tenant ID for RBAC.')
param tenantId string = subscription().tenantId

@description('Object IDs of the managed identities that need secret access.')
param secretReaderPrincipalIds array = []

@description('Object IDs of the managed identities that need secret write access (API + Worker).')
param secretWriterPrincipalIds array = []

// Soft-delete retention window: 90 days per REQ-BCP-004.
// Purge protection: prevents permanent deletion during the retention window.
var softDeleteRetentionDays = 90

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: tenantId
    enableSoftDelete: true
    softDeleteRetentionInDays: softDeleteRetentionDays
    enablePurgeProtection: true                 // REQ-BCP-004: permanent deletion not possible within retention window
    enableRbacAuthorization: true               // RBAC-only; no legacy access policies
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: true
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
      ipRules: []
      virtualNetworkRules: []
    }
  }
}

// --- RBAC assignments ---

// Key Vault Secrets User (read-only) for service identities that only consume secrets.
resource secretReaderRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in secretReaderPrincipalIds: {
  name: guid(keyVault.id, principalId, '4633458b-17de-408a-b874-0445c86b69e6')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6') // Key Vault Secrets User
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}]

// Key Vault Secrets Officer (read + write) for API and Worker managed identities.
resource secretWriterRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in secretWriterPrincipalIds: {
  name: guid(keyVault.id, principalId, 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7') // Key Vault Secrets Officer
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}]

// --- Encryption key rotation policy ---
// REQ-BCP-008: Key Vault-managed encryption keys auto-rotated every 90 days.
// This key is used for application-level envelope encryption (not storage-level KMS).
resource platformEncryptionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: keyVault
  name: 'platform-encryption-key'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: [
      'encrypt'
      'decrypt'
      'wrapKey'
      'unwrapKey'
    ]
    rotationPolicy: {
      attributes: {
        expiryTime: 'P90D'          // 90-day key lifetime per REQ-BCP-008
      }
      lifetimeActions: [
        {
          trigger: {
            timeBeforeExpiry: 'P14D' // Rotate 14 days before expiry to allow overlap window
          }
          action: {
            type: 'Rotate'
          }
        }
        {
          trigger: {
            timeBeforeExpiry: 'P7D'
          }
          action: {
            type: 'Notify'           // Alert on imminent expiry for monitoring visibility
          }
        }
      ]
    }
  }
}

// Session signing key: 90-day lifetime with 7-day overlap window per REQ-BCP-008.
// Old versions remain in Key Vault (never purged) to validate tokens issued under them.
resource sessionSigningKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: keyVault
  name: 'session-signing-key'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: [
      'sign'
      'verify'
    ]
    rotationPolicy: {
      attributes: {
        expiryTime: 'P97D'           // 90-day active + 7-day verification overlap per REQ-BCP-008
      }
      lifetimeActions: [
        {
          trigger: {
            timeBeforeExpiry: 'P14D'
          }
          action: {
            type: 'Rotate'
          }
        }
      ]
    }
  }
}

// Intent HMAC key: versioned secret for audit-trail re-verification per REQ-BCP-008 (S-13).
// Old versions must NEVER be purged — they must remain accessible for historical
// intent_ledger.payload_signature audit verification even after rotation.
// New versions are created via the admin portal token-rotation flow; rotation is NOT automatic.
resource intentHmacKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'intent-hmac-key'
  properties: {
    value: ''                        // Populated by the admin portal on first provisioning
    contentType: 'application/json' // Carries {version, created_at} metadata
    attributes: {
      enabled: true
    }
  }
}

output keyVaultId string = keyVault.id
output keyVaultUri string = keyVault.properties.vaultUri
output platformEncryptionKeyId string = platformEncryptionKey.id
output sessionSigningKeyId string = sessionSigningKey.id
