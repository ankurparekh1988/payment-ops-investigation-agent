# Without a GitHub repository (local mode): no pipeline identities, and the operator can publish the knowledge.
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
}

run "without_repository" {
  command = plan

  assert {
    condition = alltrue([
      length(module.plan_identity) == 0,
      length(module.deploy_identity) == 0,
      length(module.eval_identity) == 0,
      length(module.eval_publish_identity) == 0,
    ])
    error_message = "No pipeline identity is created without a repository."
  }

  assert {
    condition     = length(azurerm_role_assignment.operator_search) == 2
    error_message = "The operator gets Search Service Contributor and Search Index Data Contributor to publish the knowledge."
  }

  assert {
    condition     = output.deploy_identity_client_id == null && output.federated_subjects == null
    error_message = "Identity outputs are null without a repository."
  }

  assert {
    condition     = local.state_tags["deploy-from"] == "local"
    error_message = "The state storage is marked deploy-from=local, which local mode checks before changing anything."
  }
}

run "partial_repository" {
  command = plan

  variables {
    github_repository = "contoso/payment-ops"
  }

  expect_failures = [var.github_repository]
}
