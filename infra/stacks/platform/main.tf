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
  usage_table         = "usage"

  # Prices are optional; without them usage is still measured and no cost is estimated. A list, because App
  # Service setting names can't hold a deployment's hyphen.
  cost_estimate_settings = var.cost_estimate == null ? {} : merge(
    {
      PaymentOps__CostEstimate__Currency                        = var.cost_estimate.currency
      PaymentOps__CostEstimate__ContentSafetyPerThousandRecords = tostring(var.cost_estimate.content_safety_per_thousand_records)
    },
    merge([
      for i, name in sort(keys(var.cost_estimate.deployments)) : merge({
        "PaymentOps__CostEstimate__Deployments__${i}__Deployment"       = name
        "PaymentOps__CostEstimate__Deployments__${i}__InputPerMillion"  = tostring(var.cost_estimate.deployments[name].input_per_million)
        "PaymentOps__CostEstimate__Deployments__${i}__OutputPerMillion" = tostring(var.cost_estimate.deployments[name].output_per_million)
        }, var.cost_estimate.deployments[name].cached_input_per_million == null ? {} : {
        "PaymentOps__CostEstimate__Deployments__${i}__CachedInputPerMillion" = tostring(var.cost_estimate.deployments[name].cached_input_per_million)
      })
    ]...)
  )

  # Sign-in settings come from the identity deployment, which needs the deployed web app's address
  # first. Until it has run, the app is deployed without them and its sign-in smoke test fails.
  # The client ID and group ID are identifiers, not credentials: sign-in codes are redeemed with the
  # app's managed identity.
  sign_in_settings = var.entra_client_id == null ? {} : {
    AzureAd__TenantId                                      = data.azurerm_client_config.current.tenant_id
    AzureAd__ClientId                                      = var.entra_client_id
    AzureAd__ClientCredentials__0__SourceType              = "SignedAssertionFromManagedIdentity"
    AzureAd__ClientCredentials__0__ManagedIdentityClientId = azurerm_user_assigned_identity.app.client_id
    PaymentOps__Authorization__RestrictedGroupId           = var.restricted_group_id == null ? "" : var.restricted_group_id
  }
}

module "monitoring" {
  source = "../../modules/monitoring"

  workspace_name            = "log-${local.base}"
  application_insights_name = "appi-${local.base}"
  resource_group_name       = data.azurerm_resource_group.this.name
  location                  = var.location
  retention_days            = var.log_retention_days
  daily_quota_gb            = var.log_daily_quota_gb
  evaluation_endpoint_name  = "dce-${local.base}-eval"
  evaluation_rule_name      = "dcr-${local.base}-eval"
  tags                      = local.tags

  alert_name_prefix = local.base
  web_app_id        = module.web_app.id
  web_app_hostname  = module.web_app.default_hostname
  # The model is called on the OpenAI endpoint and Content Safety on the generic one: different hosts.
  ai_account_hosts = [
    for endpoint in [module.foundry.openai_endpoint, module.foundry.endpoint] : regex("^https://([^/]+)", endpoint)[0]
  ]
  action_group_id             = module.cost_guardrail.action_group_id
  availability_test_locations = var.availability_test_locations
  http5xx_threshold           = var.http5xx_threshold
  guardrail_block_threshold   = var.guardrail_block_threshold
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

module "search" {
  source = "../../modules/search-service"

  name              = "srch-${local.base}-${local.suffix}"
  resource_group_id = data.azurerm_resource_group.this.id
  location          = var.location
  sku               = var.search_sku
  semantic_search   = var.search_semantic_ranker
  tags              = local.tags
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
  app_settings = merge({
    APPLICATIONINSIGHTS_CONNECTION_STRING = module.monitoring.connection_string
    PaymentOps__Foundry__Endpoint         = module.foundry.openai_endpoint
    PaymentOps__ContentSafety__Endpoint   = module.foundry.endpoint
    PaymentOps__Search__Endpoint          = module.search.endpoint
    PaymentOps__Usage__TableEndpoint      = module.knowledge_storage.table_endpoint
    PaymentOps__Usage__DailyLimitPerUser  = tostring(var.daily_limit_per_user)
    PaymentOps__Usage__DailyLimitTotal    = tostring(var.daily_limit_total)
    PaymentOps__Usage__Paused             = tostring(var.investigations_paused)

    # App Service terminates TLS, so sign-in redirects must come from the forwarded scheme.
    ASPNETCORE_FORWARDEDHEADERS_ENABLED = "true"
  }, local.sign_in_settings, local.cost_estimate_settings)
}

# The app's daily usage counts. AzAPI, through Azure Resource Manager: the account has no keys to reach its data plane.
resource "azapi_resource" "usage_table" {
  type      = "Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01"
  name      = local.usage_table
  parent_id = "${module.knowledge_storage.id}/tableServices/default"
  body      = {}
}

# On the one table, not the account: the app reads and writes its counts and nothing else in storage.
resource "azurerm_role_assignment" "app_usage_table" {
  scope                = azapi_resource.usage_table.id
  role_definition_name = "Storage Table Data Contributor"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
  principal_type       = "ServicePrincipal"
  description          = "The web app keeps its daily usage counts here."
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

  location                   = var.location
  cutoff_enabled             = var.budget_cutoff_enabled
  cutoff_workflow_name       = "logic-${local.base}-cutoff"
  cutoff_action_group_name   = "ag-${local.base}-cutoff"
  cutoff_role_name           = "${var.name_prefix} AI Account Network Admin"
  ai_account_id              = module.foundry.account_id
  log_analytics_workspace_id = module.monitoring.workspace_id
}

# Connects Application Insights to the Foundry project for tracing. The project authenticates with
# its own managed identity; the connection string only tells Foundry where to send traces, and can't
# send telemetry on its own because ingestion requires Entra ID.
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
        ApiType                             = "Azure"
        ResourceId                          = module.monitoring.application_insights_id
        ApplicationInsightsConnectionString = module.monitoring.connection_string
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

# Screens untrusted tool text before the model reads it. The custom role is created by the bootstrap.
resource "azurerm_role_assignment" "app_prompt_shields" {
  scope                = module.foundry.account_id
  role_definition_name = "${var.name_prefix} Prompt Shields User"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
  principal_type       = "ServicePrincipal"
}

# Queries and source lookups; the app reads the index and nothing else in Search.
resource "azurerm_role_assignment" "app_search_reader" {
  scope                = module.search.id
  role_definition_name = "Search Index Data Reader"
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
