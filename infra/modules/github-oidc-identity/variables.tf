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
