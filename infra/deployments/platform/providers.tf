terraform {
  required_version = ">= 1.10"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.8"
    }
    azapi = {
      source  = "azure/azapi"
      version = "~> 2.13"
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

# Subscription and tenant come from ARM_SUBSCRIPTION_ID and ARM_TENANT_ID. Resource providers are
# registered by the bootstrap, because registration is a subscription-level action the pipeline
# identities deliberately can't perform.
provider "azurerm" {
  storage_use_azuread = true
  features {}
}

provider "azapi" {}
