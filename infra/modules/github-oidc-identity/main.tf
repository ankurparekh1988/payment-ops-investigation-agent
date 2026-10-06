terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.8"
    }
  }
}

locals {
  github_oidc_issuer = "https://token.actions.githubusercontent.com"
  azure_audience     = "api://AzureADTokenExchange"

  owner_name      = split("/", var.github_repository)[0]
  repository_name = split("/", var.github_repository)[1]

  # Repositories created after 15 July 2026 issue tokens whose subject includes the immutable
  # owner and repository IDs, so a renamed or re-created repository can't inherit this trust.
  # Older repositories use the name-only form unless they opt in.
  subject_prefix = (
    var.github_repository_id == null
    ? "repo:${var.github_repository}"
    : "repo:${local.owner_name}@${var.github_owner_id}/${local.repository_name}@${var.github_repository_id}"
  )
}

resource "azurerm_user_assigned_identity" "this" {
  name                = var.name
  resource_group_name = var.resource_group_name
  location            = var.location
  tags                = var.tags
}

# One credential per trusted workflow context. GitHub puts the context in the token's subject
# claim, so only workflows from this repository, in exactly these contexts, can sign in.
resource "azurerm_federated_identity_credential" "this" {
  for_each = var.subjects

  name                      = each.key
  user_assigned_identity_id = azurerm_user_assigned_identity.this.id
  issuer                    = local.github_oidc_issuer
  audience                  = [local.azure_audience]
  subject                   = "${local.subject_prefix}:${each.value}"
}
