output "storage_account_id" {
  description = "Resource ID of the state storage account."
  value       = azurerm_storage_account.this.id
}

output "storage_account_name" {
  description = "Name of the state storage account."
  value       = azurerm_storage_account.this.name
}

output "container_id" {
  description = "Resource ID of the state container, used as a role assignment scope."
  value       = azurerm_storage_container.this.id
}

output "container_name" {
  description = "Name of the state container."
  value       = azurerm_storage_container.this.name
}

output "operator_container_id" {
  description = "Resource ID of the operator-only state container."
  value       = azurerm_storage_container.operator.id
}

output "operator_container_name" {
  description = "Name of the operator-only state container."
  value       = azurerm_storage_container.operator.name
}
