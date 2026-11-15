# The budget cutoff: a best-effort emergency control, not a spending ceiling, because budget notifications arrive
# up to about a day after the spending (ADR 0021). Off unless cutoff_enabled: the workflow is disabled and the
# budget doesn't call it. On, the budget's 100% notification calls it, and it turns public network access off on
# the one AI account named here, which stops investigations and evaluations. The next deployment turns it back on.

data "azurerm_client_config" "current" {}

locals {
  cutoff_parameters = {
    accountId      = { value = var.ai_account_id }
    subscriptionId = { value = data.azurerm_client_config.current.subscription_id }
    budgetName     = { value = var.budget_name }
    resourceGroup  = { value = var.resource_group_name }
  }
}

# AzAPI: azurerm_logic_app_workflow can't take a complete workflow definition, and this one is tested as written.
resource "azapi_resource" "cutoff" {
  type      = "Microsoft.Logic/workflows@2019-05-01"
  name      = var.cutoff_workflow_name
  parent_id = var.resource_group_id
  location  = var.location
  tags      = var.tags

  identity {
    type = "SystemAssigned"
  }

  body = {
    properties = {
      state      = var.cutoff_enabled ? "Enabled" : "Disabled"
      definition = jsondecode(file("${path.module}/cutoff-workflow.json"))
      parameters = local.cutoff_parameters
    }
  }

  response_export_values = []
}

# Only on the one account; the role holds the account read and write and nothing in the data plane.
resource "azurerm_role_assignment" "cutoff" {
  scope                = var.ai_account_id
  role_definition_name = var.cutoff_role_name
  principal_id         = azapi_resource.cutoff.identity[0].principal_id
  principal_type       = "ServicePrincipal"
  description          = "The budget cutoff may turn this account's public network access off."
}

resource "azurerm_monitor_diagnostic_setting" "cutoff" {
  name                       = "cutoff-runs"
  target_resource_id         = azapi_resource.cutoff.id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category = "WorkflowRuntime"
  }
}

# The trigger URL carries a signature, so it's a secret: read here, kept in state and the action group, never output.
# Regenerating the workflow's access keys invalidates it; the next deployment reads the new one.
resource "azapi_resource_action" "cutoff_callback" {
  type                             = "Microsoft.Logic/workflows/triggers@2019-05-01"
  resource_id                      = "${azapi_resource.cutoff.id}/triggers/budget_notification"
  action                           = "listCallbackUrl"
  sensitive_response_export_values = ["value"]
}

resource "azurerm_monitor_action_group" "cutoff" {
  name                = var.cutoff_action_group_name
  resource_group_name = var.resource_group_name
  short_name          = "cutoff"

  # The budget's own payload: the workflow validates its fields, so the common alert schema is off.
  logic_app_receiver {
    name                    = "budget-cutoff"
    resource_id             = azapi_resource.cutoff.id
    callback_url            = azapi_resource_action.cutoff_callback.sensitive_output.value
    use_common_alert_schema = false
  }

  tags = var.tags
}
