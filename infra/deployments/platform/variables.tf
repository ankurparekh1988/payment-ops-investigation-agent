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
