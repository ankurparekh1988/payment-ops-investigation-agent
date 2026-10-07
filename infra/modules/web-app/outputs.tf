output "name" {
  description = "Web app name."
  value       = azurerm_linux_web_app.this.name
}

output "default_hostname" {
  description = "Default host name of the web app."
  value       = azurerm_linux_web_app.this.default_hostname
}

output "id" {
  description = "Resource ID of the web app."
  value       = azurerm_linux_web_app.this.id
}
