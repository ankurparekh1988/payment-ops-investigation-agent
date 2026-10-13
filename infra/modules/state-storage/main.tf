terraform {
  required_version = ">= 1.10"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.8"
    }
  }
}

resource "azurerm_storage_account" "this" {
  name                     = var.name
  resource_group_name      = var.resource_group_name
  location                 = var.location
  account_kind             = "StorageV2"
  account_tier             = "Standard"
  account_replication_type = var.replication_type

  # Entra ID only: no account keys or SAS, so every access is an auditable role assignment.
  shared_access_key_enabled       = false
  default_to_oauth_authentication = true
  min_tls_version                 = "TLS1_2"
  https_traffic_only_enabled      = true
  allow_nested_items_to_be_public = false

  # CI runners have no fixed IP range, so the endpoint stays public and Entra ID controls access.
  public_network_access = "Enabled"

  blob_properties {
    versioning_enabled = true

    delete_retention_policy {
      days = var.retention_days
    }

    container_delete_retention_policy {
      days = var.retention_days
    }
  }

  tags = var.tags

  # Terraform refuses to destroy state storage; removing it is a deliberate code change.
  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_storage_container" "this" {
  name                  = var.container_name
  storage_account_id    = azurerm_storage_account.this.id
  container_access_type = "private"

  lifecycle {
    prevent_destroy = true
  }
}

# State that holds credentials, such as demo user passwords. No pipeline identity is granted access
# to it, so it's readable only by the people given a role on it.
resource "azurerm_storage_container" "operator" {
  name                  = var.operator_container_name
  storage_account_id    = azurerm_storage_account.this.id
  container_access_type = "private"

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_management_lock" "this" {
  count = var.delete_lock_enabled ? 1 : 0

  name       = "protect-terraform-state"
  scope      = azurerm_storage_account.this.id
  lock_level = "CanNotDelete"
  notes      = "Holds Terraform state. Remove this lock deliberately before deleting the account."
}
