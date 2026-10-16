terraform {
  required_version = ">= 1.10"

  required_providers {
    azapi = {
      source  = "azure/azapi"
      version = "~> 2.13"
    }
  }
}

# AzAPI rather than azurerm_search_service: azurerm reads the admin and query keys into state even
# with local authentication disabled. AzAPI never calls a list-keys API, so no key reaches state.
resource "azapi_resource" "this" {
  type      = "Microsoft.Search/searchServices@2025-05-01"
  name      = var.name
  parent_id = var.resource_group_id
  location  = var.location
  tags      = var.tags

  # Only non-default properties are set, so nothing can drift on how Azure spells its defaults
  # (public access, one replica, one partition).
  body = {
    sku = { name = var.sku }
    properties = {
      # Entra ID only: requests with an API key are rejected.
      disableLocalAuth = true
    }
  }

  # Azure can't change a search service's tier in place.
  replace_triggers_external_values = var.sku
}
