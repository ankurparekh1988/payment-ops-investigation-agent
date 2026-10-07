variable "location" {
  description = "Azure region for every bootstrap resource."
  type        = string
}

variable "name_prefix" {
  description = "Short lowercase prefix used in resource names."
  type        = string

  validation {
    condition     = can(regex("^[a-z][a-z0-9]{2,11}$", var.name_prefix))
    error_message = "Use 3-12 lowercase letters or digits, starting with a letter (it becomes part of a storage account name)."
  }
}

variable "environment" {
  description = "Environment whose resource group and deployment identity are created, for example dev."
  type        = string

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{1,15}$", var.environment))
    error_message = "Use 2-16 lowercase letters, digits or hyphens, starting with a letter."
  }
}

variable "github_repository" {
  description = "GitHub repository (owner/name) whose workflows may sign in as the pipeline identities."
  type        = string
}

variable "github_owner_id" {
  description = "Numeric ID of the repository owner, part of the immutable OIDC subject."
  type        = number
}

variable "github_repository_id" {
  description = "Numeric ID of the repository, part of the immutable OIDC subject."
  type        = number
}

variable "developer_object_ids" {
  description = "Entra object IDs of people who run the app locally against this environment."
  type        = list(string)
  default     = []
}

variable "state_retention_days" {
  description = "Days that deleted or overwritten state can be recovered."
  type        = number
  default     = 30
}

variable "tags" {
  description = "Extra tags for every resource."
  type        = map(string)
  default     = {}
}
