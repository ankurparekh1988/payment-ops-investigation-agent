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
