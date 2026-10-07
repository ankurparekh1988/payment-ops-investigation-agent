output "account_id" {
  description = "Resource ID of the Foundry resource, used as a role assignment scope."
  value       = azurerm_cognitive_account.this.id
}

output "endpoint" {
  description = "Endpoint the application calls with an Entra ID token."
  value       = azurerm_cognitive_account.this.endpoint
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
