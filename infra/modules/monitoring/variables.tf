variable "workspace_name" {
  description = "Log Analytics workspace name."
  type        = string
}

variable "application_insights_name" {
  description = "Application Insights name."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group for the monitoring resources."
  type        = string
}

variable "location" {
  description = "Azure region."
  type        = string
}

variable "retention_days" {
  description = "Days telemetry is kept. Application Insights accepts 30, 60, 90, 120, 180, 270, 365, 550 or 730."
  type        = number

  validation {
    condition     = contains([30, 60, 90, 120, 180, 270, 365, 550, 730], var.retention_days)
    error_message = "Use 30, 60, 90, 120, 180, 270, 365, 550 or 730."
  }
}

variable "daily_quota_gb" {
  description = "Daily ingestion cap in GB. Ingestion stops for the rest of the day once reached."
  type        = number

  validation {
    condition     = var.daily_quota_gb > 0
    error_message = "Set a positive cap so telemetry costs stay bounded."
  }
}

variable "tags" {
  description = "Tags applied to the resources."
  type        = map(string)
  default     = {}
}
