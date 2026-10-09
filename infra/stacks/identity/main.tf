terraform {
  required_version = ">= 1.10"

  required_providers {
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 3.10"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.9"
    }
    time = {
      source  = "hashicorp/time"
      version = "~> 0.13"
    }
  }
}

locals {
  base = "${var.name_prefix}-${var.environment}"

  app_roles = {
    "Ops.Reader" = {
      display_name = "Ops Reader"
      description  = "Ask questions, run investigations and view citations."
    }
    "Ops.Engineer" = {
      display_name = "Ops Engineer"
      description  = "Everything a Reader can do, plus approve proposed actions such as incident tickets."
    }
    "Ops.Admin" = {
      display_name = "Ops Admin"
      description  = "Everything an Engineer can do, plus administer knowledge and demo data."
    }
  }

  # Demo users show how answers and permitted actions differ by role and group.
  demo_users = {
    reader     = { display_name = "Demo Ops Reader", role = "Ops.Reader", restricted = false }
    engineer   = { display_name = "Demo Ops Engineer", role = "Ops.Engineer", restricted = false }
    compliance = { display_name = "Demo Risk and Compliance Engineer", role = "Ops.Engineer", restricted = true }
  }
  enabled_demo_users = var.create_demo_users ? local.demo_users : {}

  web_app_url = trimsuffix(var.web_app_url, "/")

  # Entra returns users only to registered addresses: after sign-in, and after sign-out
  # (Microsoft.Identity.Web's default callback paths).
  app_urls = concat([local.web_app_url], [for url in var.local_app_urls : trimsuffix(url, "/")])
}

data "azuread_client_config" "current" {}

data "azuread_domains" "initial" {
  only_initial = true
}

module "app" {
  source = "../../modules/entra-app"

  display_name      = "Payment Ops Investigation (${var.environment})"
  role_id_namespace = "${var.name_prefix}/${var.environment}"
  app_roles         = local.app_roles
  owner_object_ids  = [var.operator_object_id]
  logout_url        = "${local.web_app_url}/signout-oidc"

  redirect_uris = flatten([for url in local.app_urls : ["${url}/signin-oidc", "${url}/signout-callback-oidc"]])

  # The deployed app redeems sign-in codes as its managed identity: no secret or certificate.
  managed_identity_credentials = {
    web-app = {
      tenant_id    = data.azuread_client_config.current.tenant_id
      principal_id = var.app_identity_principal_id
    }
  }
}

# A laptop has no managed identity, so local sign-in uses a short-lived secret that exists only
# while local addresses are configured. It's kept in user-secrets and never used by the deployed app.
resource "time_rotating" "local_secret" {
  count = length(var.local_app_urls) > 0 ? 1 : 0

  rotation_days = var.local_secret_lifetime_days
}

resource "azuread_application_password" "local" {
  count = length(var.local_app_urls) > 0 ? 1 : 0

  application_id      = module.app.application_id
  display_name        = "Local development"
  end_date            = time_rotating.local_secret[0].rotation_rfc3339
  rotate_when_changed = { rotation = time_rotating.local_secret[0].id }
}

# Members can retrieve Restricted knowledge. Assigning the group to the app is what makes Entra
# include it in the groups claim.
resource "azuread_group" "restricted" {
  display_name     = "${local.base} Risk and Compliance"
  description      = "Can retrieve Restricted knowledge in the Payment Ops Investigation Agent (${var.environment})."
  security_enabled = true
  owners           = [var.operator_object_id]
}

resource "azuread_app_role_assignment" "restricted_group" {
  app_role_id         = module.app.app_role_ids["Ops.Reader"]
  principal_object_id = azuread_group.restricted.object_id
  resource_object_id  = module.app.service_principal_object_id
}

resource "azuread_app_role_assignment" "operator" {
  app_role_id         = module.app.app_role_ids["Ops.Admin"]
  principal_object_id = var.operator_object_id
  resource_object_id  = module.app.service_principal_object_id
}

resource "random_password" "demo_user" {
  for_each = local.enabled_demo_users

  length           = 24
  special          = true
  override_special = "!@#%*-_"
}

resource "azuread_user" "demo" {
  for_each = local.enabled_demo_users

  display_name        = each.value.display_name
  user_principal_name = "${var.name_prefix}-${var.environment}-${each.key}@${data.azuread_domains.initial.domains[0].domain_name}"
  password            = random_password.demo_user[each.key].result

  # Demo accounts are shared for showing role behaviour, so their passwords don't expire. They're
  # off by default and should be removed when not in use.
  disable_password_expiration = true
}

resource "azuread_app_role_assignment" "demo_user" {
  for_each = azuread_user.demo

  app_role_id         = module.app.app_role_ids[local.demo_users[each.key].role]
  principal_object_id = each.value.object_id
  resource_object_id  = module.app.service_principal_object_id
}

resource "azuread_group_member" "demo_restricted" {
  for_each = { for key, user in azuread_user.demo : key => user if local.demo_users[key].restricted }

  group_object_id  = azuread_group.restricted.object_id
  member_object_id = each.value.object_id
}
