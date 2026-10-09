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
}

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

  redirect_uris = concat(
    ["${local.web_app_url}/signin-oidc"],
    var.local_redirect_uris,
  )
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

  display_name                = each.value.display_name
  user_principal_name         = "${var.name_prefix}-${var.environment}-${each.key}@${data.azuread_domains.initial.domains[0].domain_name}"
  password                    = random_password.demo_user[each.key].result
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
