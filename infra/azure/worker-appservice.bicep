targetScope = 'resourceGroup'

@description('Location for the Worker Service resources.')
param location string = resourceGroup().location

@description('Azure App Service plan name for the Worker Service.')
param workerPlanName string = 'signalstack-worker-plan'

@description('Azure Web App name for the Worker Service.')
param workerAppName string = 'signalstack-worker'

var workerCount = 1

resource workerPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: workerPlanName
  location: location
  sku: {
    name: 'P1v3'
    tier: 'PremiumV3'
    capacity: workerCount
  }
  kind: 'linux'
  properties: {
    reserved: true
    zoneRedundant: false
  }
}

resource workerApp 'Microsoft.Web/sites@2023-12-01' = {
  name: workerAppName
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: workerPlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      numberOfWorkers: workerCount
      minTlsVersion: '1.2'
      alwaysOn: true
    }
  }
}
