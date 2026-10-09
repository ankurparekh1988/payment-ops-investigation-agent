output "client_id" {
  description = "Application (client) ID for the web app's sign-in."
  value       = module.app.client_id
}

output "restricted_group_id" {
  description = "Object ID of the group allowed to retrieve Restricted knowledge."
  value       = azuread_group.restricted.object_id
}

output "demo_user_sign_ins" {
  description = "Demo users' sign-in names and passwords."
  value       = { for key, user in azuread_user.demo : user.user_principal_name => random_password.demo_user[key].result }
  sensitive   = true
}
