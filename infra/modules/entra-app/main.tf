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

    # Authorization code flow only. Set explicitly, because omitting the block leaves an existing
    # registration's implicit grant as it was.
    implicit_grant {
      access_token_issuance_enabled = false
      id_token_issuance_enabled     = false
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

# Lets a managed identity authenticate as this application, so the deployed app redeems sign-in
# codes without a secret. Issuer and audience are Entra's public-cloud values for managed identities.
resource "azuread_application_federated_identity_credential" "managed_identity" {
  #checkov:skip=CKV_AZURE_249:Checks GitHub Actions subjects; this trusts one managed identity by object ID.
  for_each = var.managed_identity_credentials

  application_id = azuread_application.this.id
  display_name   = each.key
  description    = "Managed identity ${each.value.principal_id}"
  audiences      = ["api://AzureADTokenExchange"]
  issuer         = "https://login.microsoftonline.com/${each.value.tenant_id}/v2.0"
  subject        = each.value.principal_id
}
