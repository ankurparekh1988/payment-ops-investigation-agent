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

variable "evaluation_endpoint_name" {
  description = "Data collection endpoint for evaluation history."
  type        = string
}

variable "evaluation_rule_name" {
  description = "Data collection rule that writes evaluation history to its table."
  type        = string
}

variable "alert_name_prefix" {
  description = "Prefix for the workbook, alert and availability test names, such as paymentops-dev."
  type        = string
}

variable "web_app_id" {
  description = "The web app whose server errors are alerted on."
  type        = string
}

variable "web_app_hostname" {
  description = "The web app's host name, for the availability test."
  type        = string
}

variable "ai_account_hosts" {
  description = "The AI account's host names (model and Content Safety), to find their throttled requests among the app's dependencies."
  type        = list(string)
}

variable "action_group_id" {
  description = "Action group every operational alert notifies."
  type        = string
}

variable "availability_test_locations" {
  description = "Locations the availability test runs from; each one is billed per run."
  type        = list(string)

  validation {
    condition     = length(var.availability_test_locations) > 0
    error_message = "The availability test needs at least one location."
  }
}

variable "http5xx_threshold" {
  description = "Server errors in 15 minutes above which the server-error alert fires."
  type        = number
}

variable "guardrail_block_threshold" {
  description = "Screen blocks in 15 minutes above which the guardrail alert fires."
  type        = number
}
