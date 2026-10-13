output "resource_group_name" {
  description = "Resource group holding the platform."
  value       = data.azurerm_resource_group.this.name
}

output "foundry_endpoint" {
  description = "OpenAI v1 API base for model calls with an Entra ID token."
  value       = module.foundry.openai_endpoint
}

output "model_deployments" {
  description = "Model deployments that were created."
  value       = module.foundry.deployment_names
}

output "content_filter_policy" {
  description = "Strict content filter policy attached to the chat deployments."
  value       = module.foundry.content_filter_policy
}

output "web_app_name" {
  description = "Name of the web app."
  value       = module.web_app.name
}

output "web_app_url" {
  description = "Public URL of the web app."
  value       = "https://${module.web_app.default_hostname}"
}

output "app_identity_client_id" {
  description = "Client ID of the identity the web app runs as."
  value       = azurerm_user_assigned_identity.app.client_id
}

output "app_identity_principal_id" {
  description = "Principal ID of the web app's identity, trusted by the Entra app registration for sign-in."
  value       = azurerm_user_assigned_identity.app.principal_id
}

output "knowledge_storage_account" {
  description = "Storage account for the knowledge corpus."
  value       = module.knowledge_storage.name
}

output "application_insights_name" {
  description = "Application Insights resource receiving telemetry."
  value       = module.monitoring.application_insights_name
}

output "action_group_id" {
  description = "Action group for alerts."
  value       = module.cost_guardrail.action_group_id
}
