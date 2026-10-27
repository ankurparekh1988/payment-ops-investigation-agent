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

output "search_endpoint" {
  description = "Endpoint of the search service holding the knowledge index."
  value       = module.search.endpoint
}

output "application_insights_name" {
  description = "Application Insights resource receiving telemetry."
  value       = module.monitoring.application_insights_name
}

output "action_group_id" {
  description = "Action group for alerts."
  value       = module.cost_guardrail.action_group_id
}

output "search_service_id" {
  description = "Resource ID of the search service; the evaluation identities' Search roles are scoped from it."
  value       = module.search.id
}

output "foundry_account_id" {
  description = "Resource ID of the Foundry account, the scope of the evaluation identity's model role."
  value       = module.foundry.account_id
}

output "evaluation_dcr_id" {
  description = "Resource ID of the evaluation history rule, the scope of the publisher's Monitoring Metrics Publisher role."
  value       = module.monitoring.evaluation_rule_id
}

output "evaluation_dcr_immutable_id" {
  description = "Immutable ID record-evaluation writes to."
  value       = module.monitoring.evaluation_rule_immutable_id
}

output "evaluation_ingestion_endpoint" {
  description = "Logs Ingestion API endpoint record-evaluation writes to."
  value       = module.monitoring.evaluation_ingestion_endpoint
}

output "content_safety_endpoint" {
  description = "The Foundry account's generic endpoint, where Prompt Shields is called; separate from the model endpoint."
  value       = module.foundry.endpoint
}
