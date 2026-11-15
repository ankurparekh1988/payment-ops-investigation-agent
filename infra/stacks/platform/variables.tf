variable "location" {
  description = "Azure region for every platform resource."
  type        = string
}

variable "name_prefix" {
  description = "Short lowercase prefix used in resource names."
  type        = string
}

variable "environment" {
  description = "Environment name, for example dev. Its resource group must already exist (bootstrap)."
  type        = string
}

variable "model_deployments" {
  description = "Model deployments keyed by deployment name, as read from the AI release manifest."
  type = map(object({
    model          = string
    version        = string
    sku            = string
    capacity       = number
    content_filter = optional(string)
  }))
}

variable "model_retirement_buffer_days" {
  description = "Refuse to deploy a model that retires within this many days."
  type        = number
}

variable "app_service_sku" {
  description = "App Service plan SKU. F1 is free; B1 and above add always-on and health-check eviction."
  type        = string
}

variable "dotnet_version" {
  description = ".NET runtime version for the web app."
  type        = string
}

variable "storage_replication_type" {
  description = "Redundancy for the knowledge storage account, for example LRS."
  type        = string
}

variable "search_sku" {
  description = "Search service tier, for example free or basic."
  type        = string
}

variable "search_semantic_ranker" {
  description = "Semantic ranker plan for the search service: disabled, free or standard."
  type        = string
}

variable "log_retention_days" {
  description = "Days telemetry is kept."
  type        = number
}

variable "log_daily_quota_gb" {
  description = "Daily telemetry ingestion cap in GB."
  type        = number
}

variable "monthly_budget" {
  description = "Monthly budget for the environment's resource group, in the billing currency."
  type        = number
}

variable "alert_email" {
  description = "Email address for budget and operational alerts."
  type        = string
  sensitive   = true
}

variable "entra_client_id" {
  description = "Client ID of the Entra app registration users sign in through (identity deployment)."
  type        = string
  default     = null

  validation {
    condition     = var.entra_client_id == null || can(regex("^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$", var.entra_client_id))
    error_message = "Must be a GUID, or unset until the identity deployment has run."
  }
}

variable "restricted_group_id" {
  description = "Object ID of the group whose members can retrieve Restricted knowledge (identity deployment)."
  type        = string
  default     = null

  validation {
    condition     = var.restricted_group_id == null || can(regex("^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$", var.restricted_group_id))
    error_message = "Must be a GUID, or unset until the identity deployment has run."
  }
}

variable "tags" {
  description = "Extra tags for every resource."
  type        = map(string)
  default     = {}
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
      input_per_million  = number
      output_per_million = number
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
