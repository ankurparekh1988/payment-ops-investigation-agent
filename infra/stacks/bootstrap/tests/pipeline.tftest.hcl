# With a GitHub repository: the pipeline identities exist and the operator publishes nothing itself.
mock_provider "azurerm" {
  mock_data "azurerm_client_config" {
    defaults = {
      subscription_id = "00000000-0000-0000-0000-000000000001"
      tenant_id       = "00000000-0000-0000-0000-000000000002"
    }
  }
}
mock_provider "random" {}

variables {
  location               = "canadacentral"
  name_prefix            = "paymentops"
  environment            = "dev"
  alert_email            = "ops@example.com"
  operator_object_id     = "00000000-0000-0000-0000-000000000003"
  eval_search_index_name = "knowledge-eval"
  github_repository      = "contoso/payment-ops"
  github_owner_id        = 1
  github_repository_id   = 2
}

run "with_repository" {
  command = plan

  assert {
    condition = alltrue([
      length(module.plan_identity) == 1,
      length(module.deploy_identity) == 1,
      length(module.eval_identity) == 1,
      length(module.eval_publish_identity) == 1,
    ])
    error_message = "Every pipeline identity is created when a repository is configured."
  }

  assert {
    condition     = length(azurerm_role_assignment.operator_search) == 0
    error_message = "The pipeline publishes the knowledge, so the operator gets no Search write roles."
  }
}
