output "client_id" {
  description = "Client ID that the workflow passes to azure/login."
  value       = azurerm_user_assigned_identity.this.client_id
}

output "principal_id" {
  description = "Object ID used for role assignments."
  value       = azurerm_user_assigned_identity.this.principal_id
}

output "id" {
  description = "Resource ID of the identity."
  value       = azurerm_user_assigned_identity.this.id
}
