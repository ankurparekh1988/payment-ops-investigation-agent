variable "budget_name" {
  description = "Name of the budget."
  type        = string
}

variable "action_group_name" {
  description = "Name of the action group that receives alerts."
  type        = string
}

variable "action_group_short_name" {
  description = "Short name shown in alert emails (up to 12 characters)."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group for the action group."
  type        = string
}

variable "resource_group_id" {
  description = "Resource group whose spending the budget tracks."
  type        = string
}

variable "monthly_amount" {
  description = "Monthly budget in the billing currency."
  type        = number

  validation {
    condition     = var.monthly_amount > 0
    error_message = "The budget must be greater than zero."
  }
}

variable "actual_cost_thresholds" {
  description = "Percentages of the budget, based on actual spend, that send an alert."
  type        = list(number)
  default     = [50, 80, 100]
}

variable "alert_email" {
  description = "Email address that receives budget and operational alerts."
  type        = string

  validation {
    condition     = can(regex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", var.alert_email))
    error_message = "Provide a valid email address."
  }
}

variable "tags" {
  description = "Tags applied to the action group."
  type        = map(string)
  default     = {}
}
