output "state_resource_group_name" {
  description = "Resource group holding Terraform state."
  value       = azurerm_resource_group.bootstrap.name
}

output "state_storage_account_name" {
  description = "Storage account holding Terraform state."
  value       = module.state.storage_account_name
}

output "state_container_name" {
  description = "Container holding Terraform state."
  value       = module.state.container_name
}

output "operator_state_container_name" {
  description = "Container for state only people may read, such as the identity deployment's."
  value       = module.state.operator_container_name
}

output "workload_resource_group_name" {
  description = "Resource group the platform stack deploys into."
  value       = azurerm_resource_group.workload.name
}

output "plan_identity_client_id" {
  description = "Client ID for read-only plan jobs."
  value       = module.plan_identity.client_id
}

output "deploy_identity_client_id" {
  description = "Client ID for approved apply and deploy jobs."
  value       = module.deploy_identity.client_id
}

output "federated_subjects" {
  description = "Exact GitHub OIDC subjects each identity trusts."
  value = {
    plan   = module.plan_identity.federated_subjects
    deploy = module.deploy_identity.federated_subjects
  }
}

output "tenant_id" {
  description = "Entra tenant ID the identities belong to."
  value       = data.azurerm_client_config.current.tenant_id
}

output "subscription_id" {
  description = "Subscription the resources were created in."
  value       = data.azurerm_client_config.current.subscription_id
}
