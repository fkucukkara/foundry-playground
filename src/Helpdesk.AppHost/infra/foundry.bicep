// Microsoft Foundry resources (AI Services account, project, model deployment) plus RBAC grants.

@description('Primary location for the Foundry resources.')
param location string = resourceGroup().location

@description('Optional principal id (e.g. your own user) to grant Foundry data-plane access for local testing.')
param principalId string = ''

@description('Principal id of the Helpdesk.Web container app identity.')
param appPrincipalId string

@description('Model to deploy for the chat agent.')
param modelName string = 'gpt-5-mini'

@description('Model version.')
param modelVersion string = '2025-08-07'

@description('Model capacity.')
param modelCapacity int = 10

var resourceToken = uniqueString(resourceGroup().id)

var abbrs = {
  aiServices: 'aisfoundry'
}

// ---------- Microsoft Foundry: account ----------
resource aiServices 'Microsoft.CognitiveServices/accounts@2025-04-01-preview' = {
  name: '${abbrs.aiServices}${resourceToken}'
  location: location
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    customSubDomainName: '${abbrs.aiServices}${resourceToken}'
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: true
    allowProjectManagement: true
  }
}

// ---------- Model Deployment ----------
resource modelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-04-01-preview' = {
  parent: aiServices
  name: modelName
  sku: {
    name: 'GlobalStandard'
    capacity: modelCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: modelName
      version: modelVersion
    }
  }
}

// ---------- Project ----------
resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-04-01-preview' = {
  parent: aiServices
  name: 'helpdesk-copilot'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {}
}

// ---------- RBAC for app managed identity ----------
resource foundryRoleForApp 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(aiServices.id, appPrincipalId, 'CognitiveServicesUser')
  scope: aiServices
  properties: {
    principalId: appPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      'a97b65f3-24c7-4388-baec-2e87135dc908'
    )
  }
}

// ---------- RBAC for developer (optional) ----------
resource foundryRoleForDeveloper 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(principalId)) {
  name: guid(aiServices.id, principalId, 'CognitiveServicesUser')
  scope: aiServices
  properties: {
    principalId: principalId
    principalType: 'User'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      'a97b65f3-24c7-4388-baec-2e87135dc908'
    )
  }
}

// ---------- Outputs ----------
output projectEndpoint string = '${aiServices.properties.endpoint}api/projects/${foundryProject.name}'
output modelDeploymentName string = modelName
