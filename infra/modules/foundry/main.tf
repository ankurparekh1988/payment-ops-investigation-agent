terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.8"
    }
  }
}

locals {
  strict_policy_name = "strict-content-filter"
  harm_categories    = ["Hate", "Sexual", "Violence", "Selfharm"]

  rai_policy_names = {
    strict  = local.strict_policy_name
    default = "Microsoft.DefaultV2"
  }
}

resource "azurerm_cognitive_account" "this" {
  name                = var.name
  resource_group_name = var.resource_group_name
  location            = var.location
  kind                = "AIServices"
  sku_name            = "S0"

  # The custom subdomain is what makes Entra ID token authentication possible.
  custom_subdomain_name      = var.name
  local_auth_enabled         = false
  project_management_enabled = true

  identity {
    type = "SystemAssigned"
  }

  tags = var.tags
}

resource "azurerm_cognitive_account_project" "this" {
  name                 = var.project_name
  cognitive_account_id = azurerm_cognitive_account.this.id
  location             = var.location
  display_name         = var.project_display_name

  identity {
    type = "SystemAssigned"
  }

  tags = var.tags
}

# Blocks anything above "Safe" in the four harm categories on prompts and completions, and jailbreak
# attempts. Synchronous filtering (mode Default) means blocked output is never streamed.
resource "azurerm_cognitive_account_rai_policy" "strict" {
  name                 = local.strict_policy_name
  cognitive_account_id = azurerm_cognitive_account.this.id
  base_policy_name     = "Microsoft.DefaultV2"
  mode                 = "Default"

  dynamic "content_filter" {
    for_each = setproduct(local.harm_categories, ["Prompt", "Completion"])
    content {
      name               = content_filter.value[0]
      source             = content_filter.value[1]
      filter_enabled     = true
      block_enabled      = true
      severity_threshold = "Low"
    }
  }

  content_filter {
    name           = "Jailbreak"
    source         = "Prompt"
    filter_enabled = true
    block_enabled  = true
  }

  content_filter {
    name           = "Protected Material Text"
    source         = "Completion"
    filter_enabled = true
    block_enabled  = true
  }

  # Annotate rather than block: the agent doesn't generate code, so a match is a signal to review.
  content_filter {
    name           = "Protected Material Code"
    source         = "Completion"
    filter_enabled = true
    block_enabled  = false
  }

  tags = var.tags
}

resource "azurerm_cognitive_deployment" "this" {
  for_each = var.model_deployments

  name                 = each.key
  cognitive_account_id = azurerm_cognitive_account.this.id

  # Models change only through a reviewed manifest change that passes evaluation (ADR 0002).
  version_upgrade_option = "NoAutoUpgrade"
  rai_policy_name        = each.value.content_filter == null ? null : local.rai_policy_names[each.value.content_filter]

  model {
    format  = "OpenAI"
    name    = each.value.model
    version = each.value.version
  }

  sku {
    name     = each.value.sku
    capacity = each.value.capacity
  }

  depends_on = [azurerm_cognitive_account_rai_policy.strict]
}
