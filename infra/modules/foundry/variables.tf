variable "name" {
  description = "Foundry resource name, also used as its globally unique subdomain."
  type        = string
}

variable "project_name" {
  description = "Foundry project name."
  type        = string
}

variable "project_display_name" {
  description = "Display name shown in the Foundry portal."
  type        = string
  default     = null
}

variable "resource_group_name" {
  description = "Resource group for the Foundry resource."
  type        = string
}

variable "location" {
  description = "Azure region."
  type        = string
}

variable "model_deployments" {
  description = <<-EOT
    Model deployments keyed by deployment name. content_filter is "strict" (the policy defined
    here), "default" (Microsoft.DefaultV2) or null for models without generated content, such as
    embeddings. capacity is in thousands of tokens per minute.
  EOT
  type = map(object({
    model          = string
    version        = string
    sku            = string
    capacity       = number
    content_filter = optional(string)
  }))

  validation {
    condition     = alltrue([for d in values(var.model_deployments) : d.content_filter == null || contains(["strict", "default"], coalesce(d.content_filter, "none"))])
    error_message = "content_filter must be \"strict\", \"default\" or null."
  }
}

variable "tags" {
  description = "Tags applied to the resources."
  type        = map(string)
  default     = {}
}
