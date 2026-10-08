module "bootstrap" {
  source = "../../stacks/bootstrap"

  location             = var.location
  name_prefix          = var.name_prefix
  environment          = var.environment
  github_repository    = var.github_repository
  github_owner_id      = var.github_owner_id
  github_repository_id = var.github_repository_id
  operator_object_id   = var.bootstrap_operator_object_id
  developer_object_ids = var.developer_object_ids
  state_retention_days = var.state_retention_days
}
