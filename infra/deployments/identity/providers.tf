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

  # Settings come from -backend-config (scripts/terraform.sh); the state key is per environment.
  backend "azurerm" {
    use_azuread_auth = true
  }
}

# The tenant comes from ARM_TENANT_ID. Creating applications, groups and users needs directory
# permissions, so this deployment is run by a person.
provider "azuread" {}
