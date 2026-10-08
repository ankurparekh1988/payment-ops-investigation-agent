terraform {
  required_version = ">= 1.10"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.8"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.9"
    }
  }
}

data "azurerm_client_config" "current" {}

locals {
  tags = merge(var.tags, {
    project    = var.name_prefix
    managed-by = "terraform"
  })

  # Built-in role definition IDs are the same in every tenant. IDs rather than names, because
  # Microsoft renames roles (Azure AI User became Foundry User) while the IDs stay fixed.
  delegable_roles = {
    "Cognitive Services OpenAI User" = "5e0bd9bd-7b93-4f28-af87-19fc36ad61bd"
    "Cognitive Services User"        = "a97b65f3-24c7-4388-baec-2e87135dc908"
    "Foundry User"                   = "53ca6127-db72-4b80-b1b0-d745d6d5456d"
    "Search Index Data Reader"       = "1407120a-92aa-4202-b7e9-c0e197c71c8f"
    "Search Index Data Contributor"  = "8ebe5a00-799e-43f5-93ac-243d3dce84a7"
    "Search Service Contributor"     = "7ca78c08-252a-4471-8644-bb5ff32d4ba0"
    "Storage Blob Data Reader"       = "2a2b9908-6ea1-4ae2-8e65-a410df84e7d1"
    "Storage Blob Data Contributor"  = "ba92f5b4-2d11-453d-a403-e96b0029c9fe"
    "Log Analytics Reader"           = "73c42c96-874c-492b-b04d-ab87d138a893"
    "Monitoring Metrics Publisher"   = "3913510d-42f4-4e42-8a64-420c390055eb"
  }
  delegable_role_ids = join(", ", values(local.delegable_roles))

  # What a developer needs to run the app locally against the environment's services.
  developer_roles = [
    "Cognitive Services OpenAI User",
    "Foundry User",
    "Search Index Data Reader",
    "Storage Blob Data Reader",
  ]
  developer_assignments = {
    for pair in setproduct(var.developer_object_ids, local.developer_roles) :
    "${pair[0]}/${pair[1]}" => { object_id = pair[0], role = pair[1] }
  }
}

# Holds what must outlive any environment: Terraform state and the pipeline identities.
resource "azurerm_resource_group" "bootstrap" {
  name     = "rg-${var.name_prefix}-bootstrap"
  location = var.location
  tags     = local.tags
}

# Created here so the pipeline identities can be scoped to this resource group rather than the
# whole subscription. The platform stack reads it instead of creating it.
resource "azurerm_resource_group" "workload" {
  name     = "rg-${var.name_prefix}-${var.environment}"
  location = var.location
  tags     = merge(local.tags, { environment = var.environment })
}

resource "random_string" "state_suffix" {
  length  = 6
  upper   = false
  special = false
}

module "state" {
  source = "../../modules/state-storage"

  name                = substr("st${var.name_prefix}tf${random_string.state_suffix.result}", 0, 24)
  resource_group_name = azurerm_resource_group.bootstrap.name
  location            = var.location
  retention_days      = var.state_retention_days
  tags                = local.tags
}

module "plan_identity" {
  source = "../../modules/github-oidc-identity"

  name                 = "id-${var.name_prefix}-gh-plan"
  resource_group_name  = azurerm_resource_group.bootstrap.name
  location             = var.location
  github_repository    = var.github_repository
  github_owner_id      = var.github_owner_id
  github_repository_id = var.github_repository_id
  tags                 = local.tags

  # Pull requests and the plan job on main. Neither can change anything.
  subjects = {
    pull-request = "pull_request"
    main-branch  = "ref:refs/heads/main"
  }
}

module "deploy_identity" {
  source = "../../modules/github-oidc-identity"

  name                 = "id-${var.name_prefix}-gh-deploy"
  resource_group_name  = azurerm_resource_group.bootstrap.name
  location             = var.location
  github_repository    = var.github_repository
  github_owner_id      = var.github_owner_id
  github_repository_id = var.github_repository_id
  tags                 = local.tags

  # Only jobs running in these GitHub environments can use this identity. Their protection rules,
  # including required reviewers, are configured with the deployment workflows.
  subjects = {
    "${var.environment}-infra" = "environment:${var.environment}-infra"
    (var.environment)          = "environment:${var.environment}"
  }
}

# --- Plan identity: read-only -------------------------------------------------------------------

# Subscription-wide read, so plans can also detect drift in the bootstrap itself (subscription role
# assignments, the custom role, provider registration). Reader can't list keys or read secrets.
resource "azurerm_role_assignment" "plan_subscription_reader" {
  scope                = "/subscriptions/${data.azurerm_client_config.current.subscription_id}"
  role_definition_name = "Reader"
  principal_id         = module.plan_identity.principal_id
  principal_type       = "ServicePrincipal"
}

# Plans run with -lock=false, so reading state is enough.
resource "azurerm_role_assignment" "plan_state_reader" {
  scope                = module.state.container_id
  role_definition_name = "Storage Blob Data Reader"
  principal_id         = module.plan_identity.principal_id
  principal_type       = "ServicePrincipal"
}

# --- Deploy identity: changes the workload resource group only ----------------------------------

resource "azurerm_role_assignment" "deploy_workload_contributor" {
  scope                = azurerm_resource_group.workload.id
  role_definition_name = "Contributor"
  principal_id         = module.deploy_identity.principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "deploy_state_contributor" {
  scope                = module.state.container_id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = module.deploy_identity.principal_id
  principal_type       = "ServicePrincipal"
}

# The platform needs role assignments (keyless access everywhere), so the deploy identity may
# assign roles, but only the allow-listed roles above and only to service principals such as
# managed identities. It cannot grant Owner or Contributor, or assign anything to users or groups.
resource "azurerm_role_assignment" "deploy_constrained_rbac_admin" {
  scope                = azurerm_resource_group.workload.id
  role_definition_name = "Role Based Access Control Administrator"
  principal_id         = module.deploy_identity.principal_id
  principal_type       = "ServicePrincipal"
  description          = "May assign only allow-listed platform roles, and only to service principals."

  condition_version = "2.0"
  condition         = <<-EOT
    (
      (
        !(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})
      )
      OR
      (
        @Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {${local.delegable_role_ids}}
        AND
        @Request[Microsoft.Authorization/roleAssignments:PrincipalType] ForAnyOfAnyValues:StringEqualsIgnoreCase {'ServicePrincipal'}
      )
    )
    AND
    (
      (
        !(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})
      )
      OR
      (
        @Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {${local.delegable_role_ids}}
        AND
        @Resource[Microsoft.Authorization/roleAssignments:PrincipalType] ForAnyOfAnyValues:StringEqualsIgnoreCase {'ServicePrincipal'}
      )
    )
  EOT
}

# --- Model preflight: read the model catalog and quota from CI ----------------------------------

# The platform's model preflight reads the regional catalog and quota, which are subscription-level.
# The deploy identity gets exactly those reads and nothing else at subscription scope.
resource "azurerm_role_definition" "model_availability_reader" {
  name        = "${var.name_prefix} Model Availability Reader"
  scope       = "/subscriptions/${data.azurerm_client_config.current.subscription_id}"
  description = "Read the regional model catalog and model quota usage."

  permissions {
    actions = [
      "Microsoft.CognitiveServices/locations/models/read",
      "Microsoft.CognitiveServices/locations/usages/read",
    ]
  }

  assignable_scopes = ["/subscriptions/${data.azurerm_client_config.current.subscription_id}"]
}

resource "azurerm_role_assignment" "model_availability_reader" {
  # The plan identity already has these reads through subscription Reader.
  for_each = {
    deploy = module.deploy_identity.principal_id
  }

  scope              = "/subscriptions/${data.azurerm_client_config.current.subscription_id}"
  role_definition_id = azurerm_role_definition.model_availability_reader.role_definition_resource_id
  principal_id       = each.value
  principal_type     = "ServicePrincipal"
}

# --- Developers ---------------------------------------------------------------------------------

# Data-plane access for people running the app locally. Granted here, by an Owner, because the
# deploy identity is deliberately unable to assign roles to users.
resource "azurerm_role_assignment" "developer" {
  for_each = local.developer_assignments

  scope                = azurerm_resource_group.workload.id
  role_definition_name = each.value.role
  principal_id         = each.value.object_id
  principal_type       = "User"
}

# --- Operator ---------------------------------------------------------------------------------

# The bootstrap operator needs data-plane access to migrate this stack's state and to plan from a
# workstation. Configured explicitly so the desired state doesn't depend on who runs the plan.
resource "azurerm_role_assignment" "operator_state_contributor" {
  scope                = module.state.container_id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = var.operator_object_id
}
