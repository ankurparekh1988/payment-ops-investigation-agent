# Operations: the workbook, four alerts and an availability test, all over the app's own telemetry. Every alert goes
# to the action group the budget uses. Log search alerts run every 15 minutes, the least often Azure allows them to
# be charged at their lowest rate; the availability test runs from as few locations as the variable sets.

resource "azurerm_application_insights_workbook" "operations" {
  name                = uuidv5("url", "https://${var.alert_name_prefix}/workbooks/operations")
  resource_group_name = var.resource_group_name
  location            = var.location
  display_name        = "${var.alert_name_prefix} operations"
  source_id           = lower(azurerm_application_insights.this.id)
  category            = "workbook"
  data_json           = templatefile("${path.module}/workbook.json", { application_insights_id = azurerm_application_insights.this.id })
  tags                = var.tags
}

# App Service's own 5xx count: Application Insights' failed requests include 4xx, which are the caller's to fix.
resource "azurerm_monitor_metric_alert" "server_errors" {
  name                = "${var.alert_name_prefix}-server-errors"
  resource_group_name = var.resource_group_name
  scopes              = [var.web_app_id]
  description         = "The web app answered with HTTP 5xx more often than expected. Runbook: Server errors."
  severity            = 2
  frequency           = "PT15M"
  window_size         = "PT15M"

  criteria {
    metric_namespace = "Microsoft.Web/sites"
    metric_name      = "Http5xx"
    aggregation      = "Total"
    operator         = "GreaterThan"
    threshold        = var.http5xx_threshold
  }

  action {
    action_group_id = var.action_group_id
  }

  tags = var.tags
}

resource "azurerm_monitor_scheduled_query_rules_alert_v2" "model_throttling" {
  name                 = "${var.alert_name_prefix}-model-throttling"
  resource_group_name  = var.resource_group_name
  location             = var.location
  scopes               = [azurerm_log_analytics_workspace.this.id]
  description          = "Requests to the AI account's model or Content Safety were throttled (HTTP 429). Runbook: Model throttling."
  severity             = 2
  evaluation_frequency = "PT15M"
  window_duration      = "PT15M"

  criteria {
    query                   = "AppDependencies | where Target has_any (${join(", ", [for host in var.ai_account_hosts : "'${host}'"])}) and ResultCode == '429'"
    time_aggregation_method = "Count"
    operator                = "GreaterThan"
    threshold               = 0
  }

  action {
    action_groups = [var.action_group_id]
  }

  tags = var.tags
}

resource "azurerm_monitor_scheduled_query_rules_alert_v2" "guardrail_blocks" {
  name                 = "${var.alert_name_prefix}-guardrail-blocks"
  resource_group_name  = var.resource_group_name
  location             = var.location
  scopes               = [azurerm_log_analytics_workspace.this.id]
  description          = "More questions blocked or tool text withheld than usual. Runbook: Guardrail block spike."
  severity             = 3
  evaluation_frequency = "PT15M"
  window_duration      = "PT15M"

  criteria {
    query                   = "AppMetrics | where Name == 'paymentops.screen.blocks' | summarize Blocks = sum(Sum)"
    time_aggregation_method = "Total"
    metric_measure_column   = "Blocks"
    operator                = "GreaterThan"
    threshold               = var.guardrail_block_threshold
  }

  action {
    action_groups = [var.action_group_id]
  }

  tags = var.tags
}

# The liveness endpoint answers without sign-in and touches no paid service.
resource "azurerm_application_insights_standard_web_test" "health" {
  name                    = "${var.alert_name_prefix}-health"
  resource_group_name     = var.resource_group_name
  location                = var.location
  application_insights_id = azurerm_application_insights.this.id
  geo_locations           = var.availability_test_locations
  frequency               = 900
  timeout                 = 30
  description             = "The web app's liveness endpoint."

  request {
    url = "https://${var.web_app_hostname}/health"
  }

  validation_rules {
    expected_status_code = 200
  }

  tags = var.tags
}

# One failing location is the whole test while it runs from one; with more, this still fires on the first.
resource "azurerm_monitor_metric_alert" "availability" {
  name                = "${var.alert_name_prefix}-availability"
  resource_group_name = var.resource_group_name
  scopes              = [azurerm_application_insights_standard_web_test.health.id, azurerm_application_insights.this.id]
  description         = "The liveness endpoint failed its availability test. Runbook: Availability."
  severity            = 1
  frequency           = "PT1M"
  window_size         = "PT15M"

  application_insights_web_test_location_availability_criteria {
    web_test_id           = azurerm_application_insights_standard_web_test.health.id
    component_id          = azurerm_application_insights.this.id
    failed_location_count = 1
  }

  action {
    action_group_id = var.action_group_id
  }

  tags = var.tags
}
