output "workspace_id" {
  description = "Resource ID of the Log Analytics workspace."
  value       = azurerm_log_analytics_workspace.this.id
}

output "application_insights_id" {
  description = "Resource ID of Application Insights."
  value       = azurerm_application_insights.this.id
}

output "application_insights_name" {
  description = "Name of Application Insights."
  value       = azurerm_application_insights.this.name
}

output "connection_string" {
  description = "Where to send telemetry. Not a credential on its own: ingestion also requires an Entra ID token."
  value       = azurerm_application_insights.this.connection_string
  sensitive   = true
}

output "evaluation_rule_id" {
  description = "Resource ID of the evaluation history rule, the scope of the publisher's only write role there."
  value       = azurerm_monitor_data_collection_rule.evaluation.id
}

output "evaluation_rule_immutable_id" {
  description = "Immutable ID the Logs Ingestion API addresses the evaluation rule by."
  value       = azurerm_monitor_data_collection_rule.evaluation.immutable_id
}

output "evaluation_ingestion_endpoint" {
  description = "Logs Ingestion API endpoint for evaluation history."
  value       = azurerm_monitor_data_collection_endpoint.evaluation.logs_ingestion_endpoint
}
