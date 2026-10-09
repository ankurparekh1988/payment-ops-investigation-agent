terraform {
  required_version = ">= 1.10"

  required_providers {
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 3.10"
    }
  }
}

locals {
  # App role IDs must never change once issued, so they're derived from the role name rather than
  # generated at random.
  app_role_ids = { for value, role in var.app_roles : value => uuidv5("url", "${var.role_id_namespace}/${value}") }
}

resource "azuread_application" "this" {
  display_name     = var.display_name
  sign_in_audience = "AzureADMyOrg"
  owners           = var.owner_object_ids

  # Emit only groups assigned to this application, which avoids group overage for users in many
  # groups and keeps unrelated group IDs out of tokens.
  group_membership_claims = ["ApplicationGroup"]

  web {
    redirect_uris = var.redirect_uris
    logout_url    = var.logout_url

    # Sign-in only: the app never calls APIs on a user's behalf, so it needs an ID token and no
    # client credential.
    implicit_grant {
      id_token_issuance_enabled = true
    }
  }

  dynamic "app_role" {
    for_each = var.app_roles
    content {
      id                   = local.app_role_ids[app_role.key]
      value                = app_role.key
      display_name         = app_role.value.display_name
      description          = app_role.value.description
      allowed_member_types = ["User"]
    }
  }
}

resource "azuread_service_principal" "this" {
  client_id = azuread_application.this.client_id
  owners    = var.owner_object_ids

  # Only users and groups assigned an app role can sign in.
  app_role_assignment_required = true
}
