variable "name" {
  description = "Search service name, also its globally unique subdomain."
  type        = string

  validation {
    # Terraform's RE2 has no lookahead, so consecutive hyphens are checked separately.
    condition     = can(regex("^[a-z0-9][a-z0-9-]{0,58}[a-z0-9]$", var.name)) && !strcontains(var.name, "--")
    error_message = "Use 2-60 lowercase letters, digits or single hyphens, not starting or ending with a hyphen."
  }
}

variable "resource_group_id" {
  description = "Resource ID of the resource group for the service."
  type        = string
}

variable "location" {
  description = "Azure region."
  type        = string
}

variable "sku" {
  description = "Pricing tier, for example free or basic."
  type        = string

  validation {
    condition     = contains(["free", "basic", "standard", "standard2", "standard3"], var.sku)
    error_message = "Must be free, basic, standard, standard2 or standard3."
  }
}

variable "semantic_search" {
  description = "Semantic ranker plan: disabled, free or standard."
  type        = string

  validation {
    condition     = contains(["disabled", "free", "standard"], var.semantic_search)
    error_message = "Must be disabled, free or standard."
  }
}

variable "tags" {
  description = "Tags applied to the service."
  type        = map(string)
  default     = {}
}
