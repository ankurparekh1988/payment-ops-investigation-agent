output "id" {
  description = "Resource ID, the scope for data-plane role assignments."
  value       = azapi_resource.this.id
}

output "endpoint" {
  description = "Search endpoint for data-plane calls with an Entra token."
  value       = "https://${azapi_resource.this.name}.search.windows.net"
}
