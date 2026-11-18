# Values come from TF_VAR_* environment variables (see .env.example), never from committed files.
# Model choices come from ai/manifest.yaml instead, because they're reviewed and evaluated.

variable "location" {
  description = "Azure region, for example canadacentral."
  type        = string
}

variable "name_prefix" {
  description = "Short lowercase prefix used in resource names."
  type        = string
}

variable "environment" {
  description = "Environment to deploy, for example dev."
  type        = string
}

variable "app_service_sku" {
  description = "App Service plan SKU, for example F1 (free) or B1."
  type        = string
}

variable "dotnet_version" {
  description = ".NET runtime version, for example 10.0."
  type        = string
}

variable "storage_replication_type" {
  description = "Knowledge storage redundancy, for example LRS."
  type        = string
}

variable "search_sku" {
  description = "Search service tier, for example free or basic."
  type        = string
}

variable "log_retention_days" {
  description = "Days telemetry is kept, for example 30."
  type        = number
}

variable "log_daily_quota_gb" {
  description = "Daily telemetry ingestion cap in GB, for example 0.5."
  type        = number
}

variable "monthly_budget" {
  description = "Monthly budget alert amount in the billing currency, for example 10."
  type        = number
}

variable "alert_email" {
  description = "Email address for budget and operational alerts."
  type        = string
  sensitive   = true
}

variable "entra_client_id" {
  description = "Client ID of the Entra app registration, from scripts/identity.sh."
  type        = string
  default     = null
}

variable "restricted_group_id" {
  description = "Object ID of the Risk and Compliance group, from scripts/identity.sh."
  type        = string
  default     = null
}

variable "model_retirement_buffer_days" {
  description = "Refuse to deploy a model retiring within this many days, for example 90."
  type        = number
}

variable "daily_limit_per_user" {
  description = "Investigations one person may start in a UTC day."
  type        = number
  default     = 25

  validation {
    condition     = var.daily_limit_per_user > 0
    error_message = "The daily limit per person must be positive."
  }
}

variable "daily_limit_total" {
  description = "Investigations everyone together may start in a UTC day."
  type        = number
  default     = 150

  validation {
    condition     = var.daily_limit_total > 0
    error_message = "The daily total must be positive."
  }
}

variable "investigations_paused" {
  description = "Pause every investigation; nothing is investigated or counted while it's true."
  type        = bool
  default     = false
}

variable "cost_estimate" {
  description = "List prices for estimating each investigation's cost; without them usage is measured and no cost is estimated."
  type = object({
    currency                            = string
    content_safety_per_thousand_records = number
    deployments = map(object({
      input_per_million        = number
      cached_input_per_million = optional(number)
      output_per_million       = number
    }))
  })
  default = null
}

variable "budget_cutoff_enabled" {
  description = "Let the budget's 100% notification turn the AI account's network access off. Off unless set."
  type        = bool
  default     = false
}

variable "availability_test_locations" {
  description = "Locations the availability test runs from; each is billed per run, every 15 minutes."
  type        = list(string)
  default     = ["us-va-ash-azr"]
}

variable "http5xx_threshold" {
  description = "Server errors in 15 minutes above which the server-error alert fires."
  type        = number
  default     = 5
}

variable "guardrail_block_threshold" {
  description = "Screen blocks in 15 minutes above which the guardrail alert fires."
  type        = number
  default     = 10
}
