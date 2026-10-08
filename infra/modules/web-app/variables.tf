variable "name" {
  description = "Web app name, globally unique (it becomes <name>.azurewebsites.net)."
  type        = string
}

variable "plan_name" {
  description = "App Service plan name."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group for the plan and the app."
  type        = string
}

variable "location" {
  description = "Azure region."
  type        = string
}

variable "sku_name" {
  description = "App Service plan SKU, for example F1 (free), B1 or P0v3."
  type        = string
}

variable "dotnet_version" {
  description = ".NET runtime version."
  type        = string
}

variable "identity_id" {
  description = "Resource ID of the user-assigned identity the app runs as."
  type        = string
}

variable "identity_client_id" {
  description = "Client ID of that identity."
  type        = string
}

variable "health_check_path" {
  description = "Path App Service probes on tiers that support health checks."
  type        = string
  default     = "/health"
}

variable "log_retention_days" {
  description = "Days the app's HTTP logs are kept on the instance file system."
  type        = number
  default     = 7
}

variable "app_settings" {
  description = "Application settings. Non-secret values only; the app reaches services with its identity."
  type        = map(string)
  default     = {}
}

variable "tags" {
  description = "Tags applied to the resources."
  type        = map(string)
  default     = {}
}
