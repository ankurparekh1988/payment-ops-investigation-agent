terraform {
  required_version = ">= 1.10"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.8"
    }
    azapi = {
      source  = "azure/azapi"
      version = "~> 2.13"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.9"
    }
  }
}

data "azurerm_client_config" "current" {}

# Created by the bootstrap, which also scopes the pipeline identities to it.
data "azurerm_resource_group" "this" {
  name = "rg-${var.name_prefix}-${var.environment}"
}

# Some names must be globally unique, so they share a stable random suffix kept in state.
resource "random_string" "suffix" {
  length  = 5
  upper   = false
  special = false
}

locals {
  base   = "${var.name_prefix}-${var.environment}"
  suffix = random_string.suffix.result

  tags = merge(var.tags, {
    project     = var.name_prefix
    environment = var.environment
    managed-by  = "terraform"
  })

  knowledge_container = "knowledge"
}

module "monitoring" {
  source = "../../modules/monitoring"

  workspace_name            = "log-${local.base}"
  application_insights_name = "appi-${local.base}"
  resource_group_name       = data.azurerm_resource_group.this.name
  location                  = var.location
  retention_days            = var.log_retention_days
  daily_quota_gb            = var.log_daily_quota_gb
  tags                      = local.tags
}

module "foundry" {
  source = "../../modules/foundry"

  name                 = "aif-${local.base}-${local.suffix}"
  project_name         = "proj-${local.base}"
  project_display_name = "Payment Ops Investigation (${var.environment})"
  resource_group_name  = data.azurerm_resource_group.this.name
  location             = var.location
  model_deployments    = var.model_deployments
  tags                 = local.tags

  depends_on = [terraform_data.model_preflight]
}

module "knowledge_storage" {
  source = "../../modules/storage-account"

  name                = substr(replace("st${var.name_prefix}${var.environment}${local.suffix}", "-", ""), 0, 24)
  resource_group_name = data.azurerm_resource_group.this.name
  location            = var.location
  replication_type    = var.storage_replication_type
  container_names     = [local.knowledge_container]
  tags                = local.tags
}

# Separate from the app so its role assignments survive the app being replaced.
resource "azurerm_user_assigned_identity" "app" {
  name                = "id-${local.base}-app"
  resource_group_name = data.azurerm_resource_group.this.name
  location            = var.location
  tags                = local.tags
}

module "web_app" {
  source = "../../modules/web-app"

  name                = "app-${local.base}-${local.suffix}"
  plan_name           = "asp-${local.base}"
  resource_group_name = data.azurerm_resource_group.this.name
  location            = var.location
  sku_name            = var.app_service_sku
  dotnet_version      = var.dotnet_version
  identity_id         = azurerm_user_assigned_identity.app.id
  identity_client_id  = azurerm_user_assigned_identity.app.client_id
  tags                = local.tags

  # Non-secret settings only. The app reaches every service with its managed identity.
  app_settings = {
    APPLICATIONINSIGHTS_CONNECTION_STRING = module.monitoring.connection_string
    PaymentOps__Foundry__Endpoint         = module.foundry.endpoint
    PaymentOps__Foundry__ChatDeployment   = var.active_chat_deployment
    PaymentOps__Foundry__EmbedDeployment  = var.embedding_deployment
    PaymentOps__Knowledge__BlobEndpoint   = module.knowledge_storage.blob_endpoint
    PaymentOps__Knowledge__Container      = local.knowledge_container
  }
}

module "cost_guardrail" {
  source = "../../modules/cost-guardrail"

  budget_name             = "budget-${local.base}"
  action_group_name       = "ag-${local.base}"
  action_group_short_name = var.name_prefix
  resource_group_name     = data.azurerm_resource_group.this.name
  resource_group_id       = data.azurerm_resource_group.this.id
  monthly_amount          = var.monthly_budget
  alert_email             = var.alert_email
  tags                    = local.tags
}

# Connects Application Insights to the Foundry project for tracing. The project authenticates with
# its own managed identity, so no connection string or key is stored.
# AzAPI: AzureRM doesn't yet support project-level connections with ProjectManagedIdentity auth.
resource "azapi_resource" "app_insights_connection" {
  type      = "Microsoft.CognitiveServices/accounts/projects/connections@2026-09-01"
  name      = "app-insights"
  parent_id = module.foundry.project_id

  # AzAPI's embedded schema predates ProjectManagedIdentity; Azure still validates the request.
  schema_validation_enabled = false

  body = {
    properties = {
      authType      = "ProjectManagedIdentity"
      category      = "AppInsights"
      target        = module.monitoring.application_insights_id
      isSharedToAll = false
      metadata = {
        ApiType    = "Azure"
        ResourceId = module.monitoring.application_insights_id
      }
    }
  }

  depends_on = [azurerm_role_assignment.foundry_project_telemetry]
}

# --- Role assignments: every service-to-service call uses a managed identity ------------------

resource "azurerm_role_assignment" "app_foundry" {
  scope                = module.foundry.account_id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "app_knowledge_reader" {
  scope                = module.knowledge_storage.container_ids[local.knowledge_container]
  role_definition_name = "Storage Blob Data Reader"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "app_telemetry" {
  scope                = module.monitoring.application_insights_id
  role_definition_name = "Monitoring Metrics Publisher"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
  principal_type       = "ServicePrincipal"
}

# The Foundry project writes traces to, and reads them from, the connected Application Insights.
resource "azurerm_role_assignment" "foundry_project_telemetry" {
  scope                = module.monitoring.application_insights_id
  role_definition_name = "Monitoring Metrics Publisher"
  principal_id         = module.foundry.project_principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "foundry_project_trace_reader" {
  scope                = module.monitoring.application_insights_id
  role_definition_name = "Log Analytics Reader"
  principal_id         = module.foundry.project_principal_id
  principal_type       = "ServicePrincipal"
}
