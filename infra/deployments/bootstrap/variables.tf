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

variable "github_owner_id" {
  description = "Numeric ID of the repository owner (gh api repos/OWNER/NAME --jq .owner.id)."
  type        = number
}

variable "github_repository_id" {
  description = "Numeric ID of the repository (gh api repos/OWNER/NAME --jq .id)."
  type        = number
}

variable "developer_object_ids" {
  description = "Entra object IDs of developers who need data access, as a JSON list (az ad signed-in-user show --query id)."
  type        = list(string)
  default     = []
}

variable "state_retention_days" {
  description = "Days that deleted or overwritten state can be recovered."
  type        = number
  default     = 30
}
