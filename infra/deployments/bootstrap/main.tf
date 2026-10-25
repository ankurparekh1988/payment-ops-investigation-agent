locals {
  # The evaluation index's name comes from the AI release, so its writer role follows a rename.
  manifest = yamldecode(file("${path.root}/../../../ai/manifest.yaml"))
}

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

  eval_foundry_account_id = var.eval_foundry_account_id
  eval_search_service_id  = var.eval_search_service_id
  eval_search_index_name  = local.manifest.retrieval.evaluationIndex
  eval_dcr_id             = var.eval_dcr_id
}
