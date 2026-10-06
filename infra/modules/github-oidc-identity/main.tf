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
  subject                   = "repo:${var.github_repository}:${each.value}"
}
