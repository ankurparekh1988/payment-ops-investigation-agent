terraform {
  required_version = ">= 1.10"

  required_providers {
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 3.10"
    }
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.8"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.9"
    }
  }

  # Settings come from -backend-config (scripts/terraform.sh); the state key is per environment.
  backend "azurerm" {
    use_azuread_auth = true
  }
}

# Tenant and subscription come from ARM_TENANT_ID and ARM_SUBSCRIPTION_ID. Creating applications,
# groups and users needs directory permissions, so this deployment is run by a person.
provider "azuread" {}

provider "azurerm" {
  storage_use_azuread = true
  features {}
}
