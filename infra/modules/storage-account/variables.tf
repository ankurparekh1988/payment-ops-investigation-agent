variable "name" {
  description = "Storage account name: 3-24 lowercase letters and digits, globally unique."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9]{3,24}$", var.name))
    error_message = "Storage account names must be 3-24 lowercase letters or digits."
  }
}

variable "resource_group_name" {
  description = "Resource group for the storage account."
  type        = string
}

variable "location" {
  description = "Azure region."
  type        = string
}

variable "replication_type" {
  description = "Storage redundancy, for example LRS or ZRS."
  type        = string
}

variable "container_names" {
  description = "Private blob containers to create."
  type        = list(string)
  default     = []
}

variable "soft_delete_days" {
  description = "Days deleted blobs and containers can be recovered."
  type        = number
  default     = 7
}

variable "tags" {
  description = "Tags applied to the storage account."
  type        = map(string)
  default     = {}
}
