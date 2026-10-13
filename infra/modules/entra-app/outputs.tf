output "client_id" {
  description = "Application (client) ID the web app signs users in with."
  value       = azuread_application.this.client_id
}

output "service_principal_object_id" {
  description = "Object ID of the enterprise application, the target of role assignments."
  value       = azuread_service_principal.this.object_id
}

output "app_role_ids" {
  description = "App role IDs keyed by role value."
  value       = local.app_role_ids
}

output "application_id" {
  description = "Resource ID of the application, for attaching credentials."
  value       = azuread_application.this.id
}
