module "bootstrap" {
  source = "../../stacks/bootstrap"

  location             = var.location
  name_prefix          = var.name_prefix
  environment          = var.environment
  github_repository    = var.github_repository
  github_owner_id      = var.github_owner_id
  github_repository_id = var.github_repository_id
  state_retention_days = var.state_retention_days
}
