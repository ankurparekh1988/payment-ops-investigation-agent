output "client_id" {
  description = "Client ID that the workflow passes to azure/login."
  value       = azurerm_user_assigned_identity.this.client_id
}

output "principal_id" {
  description = "Object ID used for role assignments."
  value       = azurerm_user_assigned_identity.this.principal_id
}

output "federated_subjects" {
  description = "Exact subjects trusted, keyed by credential name. Compare with the token GitHub issues."
  value       = { for key, credential in azurerm_federated_identity_credential.this : key => credential.subject }
}

output "id" {
  description = "Resource ID of the identity."
  value       = azurerm_user_assigned_identity.this.id
}
