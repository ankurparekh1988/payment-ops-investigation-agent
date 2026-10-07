terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.8"
    }
  }
}

locals {
  # Free and Shared plans don't support always-on or health-check eviction.
  is_free_tier = contains(["F1", "D1"], var.sku_name)
}

resource "azurerm_service_plan" "this" {
  name                = var.plan_name
  resource_group_name = var.resource_group_name
  location            = var.location
  os_type             = "Linux"
  sku_name            = var.sku_name
  tags                = var.tags
}

resource "azurerm_linux_web_app" "this" {
  name                = var.name
  resource_group_name = var.resource_group_name
  location            = var.location
  service_plan_id     = azurerm_service_plan.this.id

  https_only                                     = true
  ftp_publish_basic_authentication_enabled       = false
  webdeploy_publish_basic_authentication_enabled = false

  # Blazor Server keeps a circuit per user; affinity keeps a user on the same instance.
  client_affinity_enabled = true

  identity {
    type         = "UserAssigned"
    identity_ids = [var.identity_id]
  }

  site_config {
    always_on           = !local.is_free_tier
    websockets_enabled  = true
    http2_enabled       = true
    ftps_state          = "Disabled"
    minimum_tls_version = "1.2"

    health_check_path                 = local.is_free_tier ? null : var.health_check_path
    health_check_eviction_time_in_min = local.is_free_tier ? null : 5

    application_stack {
      dotnet_version = var.dotnet_version
    }
  }

  app_settings = merge(var.app_settings, {
    # Tells DefaultAzureCredential which of the app's identities to use.
    AZURE_CLIENT_ID = var.identity_client_id
  })

  tags = var.tags
}
