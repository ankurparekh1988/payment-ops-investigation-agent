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

  # Settings come from -backend-config (see scripts/bootstrap.sh). The very first run uses local
  # state, which the script then migrates into the storage account it has just created.
  backend "azurerm" {
    use_azuread_auth = true
  }
}

# Subscription and tenant come from ARM_SUBSCRIPTION_ID and ARM_TENANT_ID.
provider "azurerm" {
  storage_use_azuread = true

  resource_providers_to_register = [
    "Microsoft.ManagedIdentity",
    "Microsoft.Storage",
  ]

  features {}
}
