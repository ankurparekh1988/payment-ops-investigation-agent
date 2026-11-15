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

variable "location" {
  description = "Region for the budget cutoff's workflow."
  type        = string
}

variable "cutoff_enabled" {
  description = "Whether the budget's 100% notification turns the AI account's network access off. Off unless set."
  type        = bool
  default     = false
}

variable "cutoff_workflow_name" {
  description = "Name of the budget cutoff's Logic App."
  type        = string
}

variable "cutoff_action_group_name" {
  description = "Name of the action group that calls the budget cutoff."
  type        = string
}

variable "cutoff_role_name" {
  description = "Name of the custom role, created by the bootstrap, that lets the cutoff update the AI account."
  type        = string
}

variable "ai_account_id" {
  description = "The one AI account the cutoff may turn off; fixed here, never taken from a notification."
  type        = string
}

variable "log_analytics_workspace_id" {
  description = "Where the cutoff's run history goes."
  type        = string
}
