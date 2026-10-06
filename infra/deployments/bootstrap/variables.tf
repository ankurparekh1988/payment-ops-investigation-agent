# Values come from TF_VAR_* environment variables (see .env.example), never from committed files.

variable "location" {
  description = "Azure region, for example canadacentral."
  type        = string
}

variable "name_prefix" {
  description = "Short lowercase prefix used in resource names."
  type        = string
}

variable "environment" {
  description = "Environment to prepare, for example dev."
  type        = string
}

variable "github_repository" {
  description = "GitHub repository (owner/name) allowed to sign in as the pipeline identities."
  type        = string
}

variable "state_retention_days" {
  description = "Days that deleted or overwritten state can be recovered."
  type        = number
  default     = 30
}
