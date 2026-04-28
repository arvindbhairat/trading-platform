@description('Azure Front Door profile name.')
param frontDoorProfileName string = 'signalstack-frontdoor'

@description('Azure Front Door endpoint name.')
param frontDoorEndpointName string = 'signalstack-portal'

@description('Resource location for the profile.')
param location string = 'global'

@description('WAF policy name.')
param wafPolicyName string = 'signalstack-frontdoor-waf'

@description('Custom domain host name for the portal.')
param portalHostName string

resource profile 'Microsoft.Cdn/profiles@2024-02-01' = {
  name: frontDoorProfileName
  location: location
  sku: {
    name: 'Premium_AzureFrontDoor'
  }
}

resource wafPolicy 'Microsoft.Network/frontDoorWebApplicationFirewallPolicies@2024-01-01' = {
  name: wafPolicyName
  location: location
  sku: {
    name: 'Premium_AzureFrontDoor'
  }
  properties: {
    policySettings: {
      enabledState: 'Enabled'
      mode: 'Prevention'
      requestBodyCheck: 'Enabled'
    }
    customRules: {
      rules: [
        {
          name: 'AuthRateLimit'
          priority: 10
          enabledState: 'Enabled'
          ruleType: 'RateLimitRule'
          rateLimitDurationInMinutes: 60
          rateLimitThreshold: 10
          action: 'Block'
          matchConditions: [
            {
              matchVariable: 'RequestUri'
              operator: 'BeginsWith'
              negateCondition: false
              matchValue: [
                '/api/v1/auth'
              ]
            }
          ]
        }
      ]
    }
    managedRules: {
      managedRuleSets: [
        {
          ruleSetType: 'Microsoft_DefaultRuleSet'
          ruleSetVersion: '2.1'
        }
        {
          ruleSetType: 'Microsoft_BotManagerRuleSet'
          ruleSetVersion: '1.1'
        }
      ]
    }
  }
}

resource endpoint 'Microsoft.Cdn/profiles/afdEndpoints@2024-02-01' = {
  parent: profile
  name: frontDoorEndpointName
  location: location
  properties: {
    enabledState: 'Enabled'
  }
}

resource customDomain 'Microsoft.Cdn/profiles/customDomains@2024-02-01' = {
  parent: profile
  name: '${frontDoorEndpointName}-domain'
  properties: {
    hostName: portalHostName
    tlsSettings: {
      certificateType: 'ManagedCertificate'
      minimumTlsVersion: 'TLS12'
    }
  }
}

resource securityPolicy 'Microsoft.Cdn/profiles/securityPolicies@2024-02-01' = {
  parent: profile
  name: '${frontDoorEndpointName}-security-policy'
  properties: {
    parameters: {
      type: 'WebApplicationFirewall'
      wafPolicy: {
        id: wafPolicy.id
      }
      associations: [
        {
          domains: [
            {
              id: customDomain.id
            }
            {
              id: endpoint.id
            }
          ]
          patternsToMatch: [
            '/*'
          ]
        }
      ]
    }
  }
}
