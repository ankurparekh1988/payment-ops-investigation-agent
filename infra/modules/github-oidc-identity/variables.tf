variable "name" {
  description = "Name of the user-assigned managed identity."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group that holds the identity."
  type        = string
}

variable "location" {
  description = "Azure region."
  type        = string
}

variable "github_repository" {
  description = "GitHub repository in owner/name form."
  type        = string

  validation {
    condition     = can(regex("^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$", var.github_repository))
    error_message = "Use the owner/name form, for example contoso/payments."
  }
}

variable "github_owner_id" {
  description = "Numeric ID of the repository owner. Required together with github_repository_id for repositories that use immutable OIDC subjects."
  type        = number
  default     = null
}

variable "github_repository_id" {
  description = "Numeric ID of the repository. Leave both IDs null only for repositories that still use name-only OIDC subjects."
  type        = number
  default     = null

  validation {
    condition     = (var.github_repository_id == null) == (var.github_owner_id == null)
    error_message = "Set both github_owner_id and github_repository_id, or neither."
  }
}

variable "subjects" {
  description = <<-EOT
    Workflow contexts allowed to sign in, keyed by credential name. Values are the part of the
    OIDC subject after "repo:<owner>/<name>:", for example "pull_request",
    "ref:refs/heads/main" or "environment:dev".
  EOT
  type        = map(string)

  validation {
    condition     = length(var.subjects) > 0 && length(var.subjects) <= 20
    error_message = "Provide between 1 and 20 subjects (the Azure limit per identity)."
  }
}

variable "tags" {
  description = "Tags applied to the identity."
  type        = map(string)
  default     = {}
}
