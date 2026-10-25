# Evaluation history lives outside the app, so it's readable when the app isn't.

locals {
  # Matches the harness's EvaluationRow, one row per metric per run.
  evaluation_columns = {
    TimeGenerated  = "datetime"
    EvaluationId   = "string"
    Trigger        = "string"
    Commit         = "string"
    System         = "string"
    Prompt         = "string"
    ChatModel      = "string"
    KnowledgeIndex = "string"
    Run            = "int"
    Metric         = "string"
    Value          = "real"
    Threshold      = "real"
    SampleSize     = "int"
    Direction      = "string"
    Verdict        = "string"
  }

  # The harness writes to this stream by name.
  evaluation_table  = "AiEvaluation_CL"
  evaluation_stream = "Custom-${local.evaluation_table}"
}

# Retention is the workspace's.
resource "azurerm_log_analytics_workspace_table_custom_log" "evaluation" {
  name         = local.evaluation_table
  workspace_id = azurerm_log_analytics_workspace.this.id
  description  = "AI evaluation results from main: one row per metric per run."

  dynamic "column" {
    for_each = local.evaluation_columns
    content {
      name = column.key
      # The table spells it dateTime; a data collection rule's stream spells it datetime.
      type = column.value == "datetime" ? "dateTime" : column.value
    }
  }
}

# The Logs Ingestion API needs an endpoint.
resource "azurerm_monitor_data_collection_endpoint" "evaluation" {
  name                          = var.evaluation_endpoint_name
  resource_group_name           = var.resource_group_name
  location                      = var.location
  public_network_access_enabled = true
  description                   = "Logs Ingestion API endpoint for AI evaluation history."

  tags = var.tags
}

# The publisher's only write role in Azure Monitor is on this rule.
resource "azurerm_monitor_data_collection_rule" "evaluation" {
  name                        = var.evaluation_rule_name
  resource_group_name         = var.resource_group_name
  location                    = var.location
  data_collection_endpoint_id = azurerm_monitor_data_collection_endpoint.evaluation.id
  description                 = "Writes AI evaluation history to ${local.evaluation_table}."

  stream_declaration {
    stream_name = local.evaluation_stream

    dynamic "column" {
      for_each = local.evaluation_columns
      content {
        name = column.key
        type = column.value
      }
    }
  }

  destinations {
    log_analytics {
      name                  = "evaluation-history"
      workspace_resource_id = azurerm_log_analytics_workspace.this.id
    }
  }

  data_flow {
    streams       = [local.evaluation_stream]
    destinations  = ["evaluation-history"]
    output_stream = local.evaluation_stream
    transform_kql = "source"
  }

  # The rule names the table by string; wait for it to exist.
  depends_on = [azurerm_log_analytics_workspace_table_custom_log.evaluation]

  tags = var.tags
}
