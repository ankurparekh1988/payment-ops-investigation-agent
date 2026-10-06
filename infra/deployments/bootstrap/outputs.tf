output "state_resource_group_name" {
  value = module.bootstrap.state_resource_group_name
}

output "state_storage_account_name" {
  value = module.bootstrap.state_storage_account_name
}

output "state_container_name" {
  value = module.bootstrap.state_container_name
}

output "workload_resource_group_name" {
  value = module.bootstrap.workload_resource_group_name
}

output "plan_identity_client_id" {
  value = module.bootstrap.plan_identity_client_id
}

output "deploy_identity_client_id" {
  value = module.bootstrap.deploy_identity_client_id
}

output "tenant_id" {
  value = module.bootstrap.tenant_id
}

output "subscription_id" {
  value = module.bootstrap.subscription_id
}
