output "action_group_id" {
  description = "Action group that later alerts can reuse."
  value       = azurerm_monitor_action_group.this.id
}
