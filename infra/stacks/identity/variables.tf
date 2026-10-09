variable "name_prefix" {
  description = "Short lowercase prefix used in resource names."
  type        = string
}

variable "environment" {
  description = "Environment whose web app users sign in to, for example dev."
  type        = string
}

variable "web_app_url" {
  description = "HTTPS address of the deployed web app, which users return to after signing in."
  type        = string

  validation {
    condition     = startswith(var.web_app_url, "https://")
    error_message = "The web app address must use HTTPS."
  }
}

variable "operator_object_id" {
  description = "Entra object ID of the person who runs this deployment. Owns the objects and gets Ops.Admin."
  type        = string
}

variable "app_identity_principal_id" {
  description = "Principal (object) ID of the web app's managed identity, trusted to authenticate as the application."
  type        = string
}

variable "local_app_urls" {
  description = "Base addresses the app runs on locally (for example https://localhost:7207), allowed to sign in and out."
  type        = list(string)
  default     = []
}

variable "local_secret_lifetime_days" {
  description = "Days before the local-development client secret expires; rerunning the deployment after that replaces it."
  type        = number
  default     = 30

  validation {
    condition     = var.local_secret_lifetime_days >= 1 && var.local_secret_lifetime_days <= 90
    error_message = "Keep the local-development secret short-lived: 1 to 90 days."
  }
}

variable "create_demo_users" {
  description = "Create demo users for each role, to show how answers and actions differ by role."
  type        = bool
  default     = false
}
