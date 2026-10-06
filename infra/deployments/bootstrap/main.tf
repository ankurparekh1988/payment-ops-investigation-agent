module "bootstrap" {
  source = "../../stacks/bootstrap"

  location             = var.location
  name_prefix          = var.name_prefix
  environment          = var.environment
  github_repository    = var.github_repository
  state_retention_days = var.state_retention_days
}
