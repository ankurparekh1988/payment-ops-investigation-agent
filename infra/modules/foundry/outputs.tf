output "account_id" {
  description = "Resource ID of the Foundry resource, used as a role assignment scope."
  value       = azurerm_cognitive_account.this.id
}

output "endpoint" {
  description = "The account's generic endpoint."
  value       = azurerm_cognitive_account.this.endpoint
}

output "openai_endpoint" {
  description = "Base URL for the OpenAI v1 API, which the application calls with an Entra ID token."
  value       = data.azapi_resource.account.output.properties.endpoints[local.openai_endpoint_key]
}

output "project_id" {
  description = "Resource ID of the Foundry project."
  value       = azurerm_cognitive_account_project.this.id
}

output "project_principal_id" {
  description = "Object ID of the project's managed identity."
  value       = azurerm_cognitive_account_project.this.identity[0].principal_id
}

output "content_filter_policy" {
  description = "Name of the strict content filter policy."
  value       = azurerm_cognitive_account_rai_policy.strict.name
}

output "deployment_names" {
  description = "Names of the model deployments that were created."
  value       = keys(azurerm_cognitive_deployment.this)
}
