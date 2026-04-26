// Worker App Service deployment template.
// GUARDRAIL: The Worker Service must run as exactly one instance at all times.
// Scale-out silently breaks the per-position channel invariant and causes concurrent
// writes on the same position document. See ADR-0003 and engineering-standards.md.

param location string = resourceGroup().location
param appServicePlanId string
param workerAppName string
param appSettings array = []

// Fixed at 1 — never increase. Scale-out is prohibited until ADR-0003's
// multi-instance partitioning extension is delivered and accepted in a new ADR.
var workerCount = 1

resource workerApp 'Microsoft.Web/sites@2023-01-01' = {
  name: workerAppName
  location: location
  kind: 'app'
  properties: {
    serverFarmId: appServicePlanId
    siteConfig: {
      appSettings: appSettings
      alwaysOn: true
    }
  }
}

resource workerAppConfig 'Microsoft.Web/sites/config@2023-01-01' = {
  parent: workerApp
  name: 'web'
  properties: {
    // Hardwired to workerCount — do not change to a variable or parameter.
    numberOfWorkers: workerCount
  }
}

// Explicit output so callers can assert the instance count.
// capacity: workerCount
output instanceCount int = workerCount
output appServiceId string = workerApp.id

// NOTE: Auto-scale resources are prohibited in this file.
// The Worker is a singleton by design; adding auto-scale would break position-channel safety.
// See the pre-flight script and ADR-0003 for enforcement details.
