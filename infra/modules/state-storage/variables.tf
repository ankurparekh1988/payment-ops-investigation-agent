variable "name" {
  description = "Storage account name: 3-24 lowercase letters and digits, globally unique."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9]{3,24}$", var.name))
    error_message = "Storage account names must be 3-24 lowercase letters or digits."
  }
}

variable "resource_group_name" {
  description = "Resource group that holds the storage account."
  type        = string
}

variable "location" {
  description = "Azure region."
  type        = string
}

variable "container_name" {
  description = "Blob container for state files."
  type        = string
  default     = "tfstate"
}

variable "operator_container_name" {
  description = "Blob container for state that only people may read."
  type        = string
  default     = "tfstate-operator"
}

variable "replication_type" {
  description = "Storage redundancy. ZRS keeps state available through a zone outage."
  type        = string
  default     = "ZRS"
}

variable "retention_days" {
  description = "Days that deleted or overwritten state blobs and containers can be recovered."
  type        = number
  default     = 30

  validation {
    condition     = var.retention_days >= 1 && var.retention_days <= 365
    error_message = "Retention must be between 1 and 365 days."
  }
}

variable "delete_lock_enabled" {
  description = "Add a CanNotDelete lock to the storage account."
  type        = bool
  default     = true
}

variable "tags" {
  description = "Tags applied to the storage account."
  type        = map(string)
  default     = {}
}
